# Monolith for Android 1.84.0 — preview

Signed ARM64 APK for Android 8+ (API 26). Installed name: Monolith; package: com.monolithharness.portable; version code: 118.

- Green monolith launcher/startup logo, with independent source artwork.
- Material 3 styling, project/chat sidebar and settings for providers, skills, permissions and memory.
- Native Markdown and one expandable tool summary per turn; reasoning status above the composer.
- Header model selector; expanding one-line composer with + attachments and Send/Stop.
- Common summary/planning/review/information skills, public HTTPS web tools, imported private text files and scoped memory.
- Global tool permissions: Refuse all, Ask all (default), Allow all. Disabled features and file/memory scope restrictions still apply.

[Installation, signing and limits](https://github.com/loicdelaunay/Monolith-Harness/blob/v1.84.0/docs/android-portable.md).

Download the APK and check SHA256SUMS-android.txt. No database, providers, API keys or signing key are packaged. Configure your own HTTPS provider after installing. Desktop and Android data remain independent.

This APK uses the durable production certificate; future release APKs retain it. USB/debug builds use a different key and cannot be updated in place with this APK. Uninstalling deletes private app data; preserve your current debug installation if its chats must be retained. Publishing does not replace it.

Preview limits: no local inference, terminal/desktop control, MCP, desktop synchronization, proposal review or Mermaid renderer. Documents are imported text copies; PDF/Office/binary extraction is not included. Generation is cancelled when the activity stops. Previous Pixel checks covered specific UI scenarios, not every tool/provider exchange or device. No new automated functional suite was requested for this branding release.


APK provenance: [successful GitHub build](https://github.com/loicdelaunay/Monolith-Harness/actions/runs/38078104651), source ref `android-v1.84.0` at commit `3e71b74718a0da4ad557e303c1e319d5bb68ee93`. Package identity, ARM64 ABI, minimum API 26, signature and SHA-256 manifest were checked before publication. Release certificate SHA-256: `9a86141f41f55c2419cd7e8a42e6b332899106364ecba584f61251d8aa5b206f`.
