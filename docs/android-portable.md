# Android portable branch

Development: `feature/android-portable`. Main and desktop releases are independent.

## Boundaries

- **Core**: platform-neutral conversation/provider contracts, API catalogues, streaming parser, compatibility, retries, context, wire history and API chat orchestration. No native dependencies or bundled models.
- **Core.Desktop**: existing storage/migrations, encryption, agent/tool execution, Python, terminal, Git, local models, ONNX, desktop control and hosting. References Core.
- **Core.Mobile**: Android private SQLite storage and Android Keystore credentials. References Core only.
- **MonolithHarnessGui.Portable**: Android-only Uno UI. References Mobile and Core; never Desktop.

Desktop namespaces, SQLite entities/table names, migration namespaces and resource names are preserved. The Desktop composition root supplies the existing process and token-usage adapters to `ChatEngine`; Mobile explicitly supplies an API-only runtime. Native dependencies/resources stay in Desktop.

The full solution includes all projects. `MonolithHarness.Desktop.slnx` needs no Android workload. `MonolithHarness.Mobile.slnx` includes no desktop hosts/tools.

## Preview scope

Android 8+ (API 26), ARM64 package. API chat, streaming, project/chat history, provider settings and JPEG/PNG/WebP/GIF attachments up to 8 MiB each. Configure an HTTPS endpoint, model and API key. Data is stored in Android private application storage, independently of desktop profiles; API keys use a non-exportable Android Keystore AES key. Android backups are disabled.

The first mobile interface displays selectable plain text. Rich Markdown/Mermaid, general documents, local inference, system tools/skills, synchronization and proposal review are not exposed yet. Desktop retains its rich rendering/tools. On activity stop, generation is cancelled and the partial reply is saved; mobile does not promise background generation. Process termination may discard text received since the last save; unfinished records are shown as interrupted after restart.

## Local build

.NET 10, Android workload, JDK 17, SDK 36:

```powershell
dotnet build MonolithHarness.Mobile.slnx -c Release
./publish-android.ps1
```

The helper publishes a preview in `artifacts/TEMP/android-portable`. Without explicit signing properties, it uses the build machine's development signing identity. Stable release updates require a persistent identity. Desktop continues to publish into `artifacts/GUI` and `artifacts/CLI` with the existing helpers.

## GitHub on demand

`Android portable (manual)` uses only `workflow_dispatch`: no automatic run on push or desktop release tags. Its definition becomes visible in Actions once merged into the default branch; choose the branch/ref to build.

An empty `release_tag` produces preview artifacts only. To attach a signed APK to an existing release, configure `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, `ANDROID_STORE_PASSWORD`, then specify `v<version>` or `android-v<version>`. The tag must match the application version and exact build commit. Existing Android assets are never overwritten. APK and SHA-256 manifest uploads are verified against GitHub digests; signing files are removed from the runner.

No public Android release is created as part of this branch implementation. Compilation/packaging do not confirm runtime behavior on a phone or emulator.
