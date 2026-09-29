# OhMyHarness CLI 1.29.0

Choose `OhMyHarness-CLI-v1.29.0-win-x64.zip`, `OhMyHarness-CLI-v1.29.0-osx-arm64.tar.gz` (Apple Silicon), `OhMyHarness-CLI-v1.29.0-osx-x64.tar.gz` (Intel), or `OhMyHarness-CLI-v1.29.0-linux-x64.tar.gz`. Extract into a writable folder and run `omh` (`omh.exe` on Windows). Enter `/connect` to configure a provider. Check your archive against the attached `SHA256SUMS.txt`.

This release shares the **1.29.0 swarm engine** with the [GUI release](https://github.com/loicdelaunay/OhMyHarness/releases/tag/v1.29.0): Auto evaluates delegation before a request, teams can use additional waves and descendants, and workers inherit enabled tools and permissions. Long work compacts its context for continuation. The shared default behavior is Proactive; the GUI Agents settings page configures Selective, Balanced, Proactive or Swarm behavior and custom instructions.

The former six-agent/eight-step caps are replaced by configurable safeguards: **64 agents per request, 8 parallel model requests, 3 delegation levels and 200 steps per worker** by default. Cancellation, Plan mode, sandbox restrictions and repeated-call protection remain active. OpenCode workers use their authorized native tools; native nested delegation remains disabled for application-managed workers because their descendants cannot be counted in the shared budget.

The shared engine also includes the 1.28.0 Git read/write controls, project resource fixes, German/Spanish reply languages and saved conversation goals. See the [full changelog](https://github.com/loicdelaunay/OhMyHarness/blob/main/CHANGELOG.md).

Each archive contains only the executable, MIT license and startup instructions. It includes no database, profiles, credentials, logs or user data. macOS builds are unsigned and not notarized. Close the CLI and replace only its executable when updating; preserve your existing portable data.
