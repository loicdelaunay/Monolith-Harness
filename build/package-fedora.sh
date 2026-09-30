#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
version="$(sed -n 's/.*<ApplicationDisplayVersion>\([^<]*\)<\/ApplicationDisplayVersion>.*/\1/p' src/OhMyHarness.App/OhMyHarness.App.csproj | head -n 1)"
cli_version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/OhMyHarness.Cli/OhMyHarness.Cli.csproj | head -n 1)"
test -n "$version" && test "$version" = "$cli_version"
root="$(pwd -P)/artifacts/TEMP/release-packages"
mkdir -p "$root/GUI" "$root/CLI"
for channel in GUI CLI; do
  executable=MonolithHarness
  prefix=OhMyHarness
  if [[ "$channel" == CLI ]]; then prefix=OhMyHarness-CLI; fi
  source="artifacts/$channel/$executable"
  test -s "$source"
  stage="$(mktemp -d "$root/stage-$channel-linux-x64.XXXXXXXX")"
  cleanup() {
    local resolved
    resolved="$(realpath -m "$stage")"
    [[ "$(dirname "$resolved")" == "$root" && "$resolved" == "$root"/stage-"$channel"-linux-x64.* ]]
    rm -rf -- "$resolved"
  }
  trap cleanup EXIT
  cp "$source" "$stage/$executable"
  cp LICENSE "$stage/LICENSE"
  if [[ "$channel" == GUI ]]; then start='Run ./MonolithHarness and open Settings > Providers.'; else start='Run ./MonolithHarness from a terminal, then /connect to configure your provider.'; fi
  cat > "$stage/README.txt" <<EOF
Monolith Harness $channel $version (linux-x64)

Extract into a writable, private folder. $start
Linux GUI uses X11 or XWayland; install GTK3 and WebKitGTK for its browser.
No separate .NET or Python installation is required.

To update, close the application and replace only $executable. Keep your
database.sqlite, keys and existing resources. This download has no user data.
The chat model is not bundled; configure a provider. Optional integrations
such as Git, Docker/Podman, OpenCode and MCP retain their own prerequisites.

https://github.com/loicdelaunay/Monolith-Harness
EOF
  chmod +x "$stage/$executable"
  archive="$root/$channel/$prefix-v$version-linux-x64.tar.gz"
  test ! -e "$archive"
  tar -czf "$archive" -C "$stage" "$executable" LICENSE README.txt
  (cd "$root/$channel" && sha256sum "$(basename "$archive")" > SHA256SUMS-linux-x64.txt)
  cleanup
  trap - EXIT
  echo "Packaged $archive"
done
