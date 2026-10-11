#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
version="$(sed -n 's/.*<ApplicationDisplayVersion>\([^<]*\)<\/ApplicationDisplayVersion>.*/\1/p' src/MonolithHarness.App/MonolithHarness.App.csproj | head -n 1)"
cli_version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/MonolithHarness.Cli/MonolithHarness.Cli.csproj | head -n 1)"
test -n "$version" && test "$version" = "$cli_version"
channels=("$@")
if [[ ${#channels[@]} == 0 ]]; then channels=(GUI CLI); fi
for channel in "${channels[@]}"; do
  case "$channel" in GUI|CLI) ;; SERVER)
    server_version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/MonolithHarness.Server/MonolithHarness.Server.csproj | head -n 1)"
    test "$version" = "$server_version" ;;
    *) echo "Unknown release channel: $channel" >&2; exit 1 ;;
  esac
done
root="$(pwd -P)/artifacts/TEMP/release-packages"
for channel in "${channels[@]}"; do
  mkdir -p "$root/$channel"
  executable=MonolithHarness
  prefix=MonolithHarness
  if [[ "$channel" == CLI ]]; then prefix=MonolithHarness-CLI; fi
  if [[ "$channel" == SERVER ]]; then prefix=MonolithHarness-Server; fi
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
  if [[ "$channel" == SERVER ]]; then
    cat > "$stage/README.txt" <<EOF
Monolith Harness Server $version (linux-x64)

This server is optional: it adds on-premise cloud and collaborative features.
Extract into a writable, private folder, run ./MonolithHarness, then open
http://127.0.0.1:5800. Set your own administrator password before remote access;
see the Server guide for first sign-in, HTTPS and optional container execution.
No separate .NET installation is required.

To update, stop the server and replace only $executable. Back up and keep
database.sqlite, server.json and the entire workspace/ folder (including keys).
This download includes no user data or configured provider credentials.

https://github.com/loicdelaunay/Monolith-Harness/blob/main/docs/server.md
EOF
  else
    cat > "$stage/README.txt" <<EOF
Monolith Harness $channel $version (linux-x64)

Extract into a writable, private folder. $start
Linux GUI uses X11 or XWayland; install GTK3 and WebKitGTK for its browser.
No separate .NET or Python installation is required.

To update, close all application instances and replace only $executable. Keep your
database.sqlite, keys, workspace/ and existing resources. This download has no user data.
Startup groups managed resource folders in workspace/; folderless chats get their
own workspace/conversations/chat-ID/ directory, with files retained across restarts.
The chat model is not bundled; configure a provider. Optional integrations
such as Git, Docker/Podman, OpenCode and MCP retain their own prerequisites.

https://github.com/loicdelaunay/Monolith-Harness
EOF
  fi
  chmod +x "$stage/$executable"
  archive="$root/$channel/$prefix-v$version-linux-x64.tar.gz"
  test ! -e "$archive"
  tar -czf "$archive" -C "$stage" "$executable" LICENSE README.txt
  (cd "$root/$channel" && sha256sum "$(basename "$archive")" > SHA256SUMS-linux-x64.txt)
  cleanup
  trap - EXIT
  echo "Packaged $archive"
done
