"""Publish validated GUI/CLI artifacts using the runner's repository-scoped GitHub token."""

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET
import zipfile


def gh(*args, allow_failure=False):
    result = subprocess.run(["gh", *args, "--repo", os.environ["GH_REPO"]] if args[0] == "release"
                            else ["gh", *args], capture_output=True, text=True)
    if result.returncode and not allow_failure:
        raise RuntimeError(result.stderr.strip())
    return result


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_archive(path, windows):
    expected = {"MonolithHarness.exe" if windows else "MonolithHarness", "LICENSE", "README.txt"}
    if windows:
        with zipfile.ZipFile(path) as archive:
            if len(archive.infolist()) != 3 or set(archive.namelist()) != expected:
                raise RuntimeError(f"Unexpected archive contents: {path.name}")
    else:
        with tarfile.open(path, "r:gz") as archive:
            members = archive.getmembers()
            if len(members) != 3 or {member.name for member in members} != expected or any(not member.isfile() for member in members):
                raise RuntimeError(f"Unexpected archive contents: {path.name}")
            if not archive.getmember("MonolithHarness").mode & 0o111:
                raise RuntimeError(f"Launcher is not executable: {path.name}")


def main():
    tag = os.environ["RELEASE_TAG"]
    if not re.fullmatch(r"v\d+\.\d+\.\d+", tag):
        raise RuntimeError("A stable version tag is required.")
    version = tag[1:]
    root = Path(__file__).resolve().parent.parent
    gui = ET.parse(root / "src/MonolithHarness.App/MonolithHarness.App.csproj").findtext(".//ApplicationDisplayVersion")
    cli = ET.parse(root / "src/MonolithHarness.Cli/MonolithHarness.Cli.csproj").findtext(".//Version")
    if gui != version or cli != version:
        raise RuntimeError("Both application versions must match the release tag.")
    if os.environ.get("RELEASE_ARTIFACT_RUN"):
        repo = os.environ["GH_REPO"]
        run = json.loads(gh("api", f"repos/{repo}/actions/runs/{os.environ['RELEASE_ARTIFACT_RUN']}").stdout)
        ref = json.loads(gh("api", f"repos/{repo}/git/ref/tags/{tag}").stdout)["object"]
        for _ in range(4):
            if ref["type"] == "commit":
                break
            if ref["type"] != "tag":
                raise RuntimeError("Unexpected release tag target.")
            ref = json.loads(gh("api", f"repos/{repo}/git/tags/{ref['sha']}").stdout)["object"]
        if ref["type"] != "commit" or run["head_sha"] != ref["sha"] or run["path"] != ".github/workflows/release-packages.yml":
            raise RuntimeError("Artifacts must come from the build of this exact version tag.")
    notes = root / f"docs/releases/{tag}.md"
    if not notes.is_file():
        raise RuntimeError("Release notes are missing.")
    aliases = json.loads((root / "build/LegacyReleaseNames.json").read_text())
    work = root / "artifacts/TEMP/publishing-packages"
    work.mkdir(parents=True, exist_ok=False)
    channels = []
    for channel in ("GUI", "CLI"):
        prefix = "MonolithHarness" if channel == "GUI" else "MonolithHarness-CLI"
        output = work / channel
        output.mkdir()
        for runtime, extension in (("win-x64", "zip"), ("linux-x64", "tar.gz")):
            source = root / f"artifacts/TEMP/downloaded-packages/{prefix}-{runtime}"
            expected = [f"{prefix}-{tag}-{runtime}.{extension}"]
            if runtime == "win-x64":
                expected.append(f"{aliases[channel]}-{tag}-{runtime}.{extension}")
            manifest = source / f"SHA256SUMS-{runtime}.txt"
            declared = {}
            for line in manifest.read_text().splitlines():
                sha, name = line.split(None, 1)
                declared[name.strip()] = sha
            for name in expected:
                path = source / name
                validate_archive(path, runtime == "win-x64")
                if digest(path) != declared.get(name):
                    raise RuntimeError(f"Checksum mismatch: {name}")
                shutil.copyfile(path, output / name)
            if runtime == "win-x64" and digest(source / expected[0]) != digest(source / expected[1]):
                raise RuntimeError("Compatibility alias differs from the canonical download.")
        archives = sorted(output.iterdir())
        (output / "SHA256SUMS.txt").write_text("".join(f"{digest(path)}  {path.name}\n" for path in archives), encoding="ascii")
        assets = sorted(output.iterdir())
        channels.append((channel, tag if channel == "GUI" else f"cli-{tag}", assets))

    # Both drafts exist and both uploads pass validation before either becomes public.
    for channel, release_tag, assets in channels:
        probe = gh("release", "view", release_tag, "--json", "isDraft,apiUrl", allow_failure=True)
        if not probe.returncode:
            if not json.loads(probe.stdout)["isDraft"]:
                raise RuntimeError(f"Refusing to replace an already published release: {release_tag}")
        elif "HTTP 404" in probe.stderr or "release not found" in probe.stderr.lower():
            gh("release", "create", release_tag, "--draft", "--verify-tag", "--title", f"Monolith Harness {channel} {version}", "--notes-file", str(notes))
        else:
            raise RuntimeError(probe.stderr.strip())
        gh("release", "upload", release_tag, *(str(path) for path in assets), "--clobber")
        # The REST /releases/tags endpoint only exposes published releases; use the draft's ID.
        metadata = json.loads(gh("release", "view", release_tag, "--json", "apiUrl").stdout)
        release = json.loads(gh("api", metadata["apiUrl"]).stdout)
        remote = {item["name"]: item for item in release["assets"]}
        if set(remote) != {path.name for path in assets}:
            raise RuntimeError(f"Unexpected release assets: {release_tag}")
        for path in assets:
            item = remote[path.name]
            if item["state"] != "uploaded" or item["size"] != path.stat().st_size or item.get("digest") != f"sha256:{digest(path)}":
                raise RuntimeError(f"Remote asset verification failed: {path.name}")
        print(f"Validated draft {release_tag}: {len(assets)} assets", flush=True)
    gh("release", "edit", f"cli-{tag}", "--draft=false", "--latest=false")
    gh("release", "edit", tag, "--draft=false", "--latest=true")
    print(f"Published GUI and CLI {version}", flush=True)


if __name__ == "__main__":
    main()
