# MonolithHarness CLI 1.26.0

Choose the archive for your system: `win-x64.zip`, `osx-arm64.tar.gz` (Apple Silicon), `osx-x64.tar.gz` (Intel), or `linux-x64.tar.gz` (Fedora 44). Extract into a writable folder and run `omh` (`omh.exe` on Windows) in a terminal. Run `/connect` to configure a model provider. Check the archive against `SHA256SUMS.txt`.

The CLI and GUI use the same 1.26.0 engine and portable database. This update includes the new conversation, agent, tool, retry and logging changes since 1.21.2. The CLI can follow a custom palette saved in the GUI's Appearance settings.

The download contains only the executable, license and startup instructions. It contains no database, profile, keys or user data. The chat model is configured separately. macOS builds are unsigned and not notarized. Automatic executable replacement is available on Windows; on Mac or Linux, close the application and replace only the executable.

See the [full changelog](../../CHANGELOG.md), [CLI guide](../cli.md), [macOS guide](../macos.md) and [Fedora guide](../fedora.md).
