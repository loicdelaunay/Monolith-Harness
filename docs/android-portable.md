# Monolith for Android — preview 1.84.0

Desktop and Android development now share `main`; `feature/android-portable` has been merged. [Download the signed ARM64 APK](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/android-v1.84.0); source tag `v1.84.0` contains desktop and mobile projects. The Android release is marked as a preview and does not replace desktop downloads. The default branch contains both platform hosts; use `main` or a release tag to build Android.

## Boundaries

- **Core**: platform-neutral conversation/provider contracts, API catalogues, streaming parser, compatibility, retries, context, wire history and API chat orchestration with optional platform tools, plus the common Summarization/Planning/Review instructions. No native dependencies or bundled models.
- **Core.Desktop**: existing storage/migrations, encryption, agent/tool execution, Python, terminal, Git, local models, ONNX, desktop control and hosting. References Core.
- **Core.Mobile**: Android private SQLite storage and Android Keystore credentials. References Core only.
- **MonolithHarnessGui.Portable**: Android-only Uno UI. References Mobile and Core; never Desktop.

Desktop namespaces, SQLite entities/table names, migration namespaces and resource names are preserved. The Desktop composition root supplies the existing process and token-usage adapters to `ChatEngine`; Mobile explicitly supplies an API-only runtime. Native dependencies/resources stay in Desktop.

The full solution includes all projects. `MonolithHarness.Desktop.slnx` needs no Android workload. `MonolithHarness.Mobile.slnx` includes no desktop hosts/tools.

## Preview scope

Android 8+ (API 26), ARM64 package. API chat, streaming, project/chat history, provider settings and JPEG/PNG/WebP/GIF attachments up to 8 MiB each. Configure an HTTPS endpoint, model and API key. Data is stored in Android private application storage, independently of desktop profiles; API keys use a non-exportable Android Keystore AES key. Android backups are disabled.

The current interface has native Markdown messages, a project/chat sidebar, settings, common skills, grouped tool summaries and a compact composer with model selection in the header. Mermaid rendering, PDF/Office and other binary document extraction, local inference, desktop system tools, MCP, synchronization and proposal review are not exposed yet. Desktop retains its rich rendering/tools. On activity stop, generation is cancelled and the partial reply is saved; mobile does not promise background generation. Process termination may discard text received since the last save; unfinished records are shown as interrupted after restart.

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

## Device startup check

On 2026-10-10 the ARM64 Release APK was installed through USB debugging on a Pixel 9 Pro Fold. A native `NativeApplication.n_onCreate` registration failure was corrected by disabling `AndroidEnableMarshalMethods`; changing this setting requires a clean Android rebuild. Installation and activity startup then succeeded and the process remained running. This does not validate API exchanges or the full mobile feature set.

## Mobile presentation (1.78.1)

The Android host uses [Uno Material 3 styles](https://platform.uno/docs/articles/external/uno.themes/doc/material-controls-styles.html), Android wallpaper palette colors on API 31+ (Material fallback on older versions), a system light/dark theme selected at startup, rounded expressive surfaces and vector Google Material icons. Interactive icon buttons keep 48 dp hit areas; titles and model labels remain on one line and truncate when needed.

`MobileLayout` calculates status/navigation/cutout/keyboard insets in logical pixels, accounting for any resize already applied by Android. Compact layouts hide secondary navigation, very short keyboard layouts retain the composer first, and the reading column is capped at 880 dp. In-page modal forms share the same safe area with scrollable content and fixed actions. Back dismisses the keyboard before the modal.

Run the platform-independent layout checks without an Android workload:

```powershell
dotnet run --project tests/MonolithHarness.Mobile.Tests -c Release
```

The 125 checks cover portrait, narrow widths, landscape, unfolded sizes, full/partial/no IME resizing, modal space, touch targets and transient invalid dimensions. They validate layout calculations, not Android rendering or API exchanges. See the local Pixel visual validation report for real device screenshots and the tested scope. Material 3 Expressive shapes and hierarchy are adapted to Uno; this is not a replacement of the renderer with the complete native Android Expressive component suite.

## Android branding

The installed application is named **Monolith**. Its Android launcher icon is generated with Uno.Resizetizer from the dedicated green `src/MonolithHarnessGui.Portable/Assets/logo.png` brand asset (since 1.84.0), with density-specific and adaptive variants. The portable host only references the image file, not the Desktop project. The package ID remains `com.monolithharness.portable`, so updating an existing installation preserves its private conversation data.

## Mobile settings and skills (1.79.0)

The toolbar settings button opens Général, Fournisseurs, Skills, Autorisations, Mémoire and À propos. Mobile settings are stored atomically in the private app directory; API credentials remain encrypted by Android Keystore in the provider database. Text size, answer language, tool activity and the per-message tool-round limit are configurable.

- `/summary`, `/planning` and `/review` reuse the same neutral definitions as desktop. `/informations` exposes selected current date/time, timezone, Android version/API and locale categories; automatic insertion can be disabled. No device identifier, account, contacts or installed-app inventory is collected.
- `/web` exposes `web_search` (DuckDuckGo public HTML results) and `web_read` (public HTTPS text/HTML/JSON with line ranges). Network approval is on by default. Redirects and connection-time DNS addresses are checked; local/private endpoints and non-HTTPS URLs are unavailable. Responses, timeouts, output lengths and cache sizes are bounded. JavaScript rendering/authenticated pages can be unavailable; errors are returned explicitly.
- `/sources` exposes list/read/literal search/write/exact edit tools for the current conversation's private text documents. Native [Android document selection](https://developer.android.com/training/data-storage/shared/documents-files) imports a UTF-8/UTF-16 copy (2 MiB per file, up to 60 files and 32 MiB per discussion). No broad storage permission is requested. PDF/Office/binary extraction is not included. AI changes affect only the private copy; the user previews and exports it through Android's Save as picker. Writing can be disabled and asks approval by default.
- `/memory` exposes scoped search/save/delete. Conversation memories are visible only in their chat; Project memories only in that project; Shared memories are available only with the explicit setting enabled. Writes/deletes ask approval by default. The Mémoire page lets the user search, add and delete visible entries. A separate private SQLite database is created without changing the existing conversation database schema.

Slash suggestions insert the invocation and a space, without sending. The requested skill is prioritized at send time; a disabled skill stays disabled. The orchestration loop persists each assistant tool batch and its tool replies before continuing the model response. Cancellation closes an approval and records replies for interrupted tool calls. Desktop's native agent tools, permission checks, storage and migrations stay in Core.Desktop.

This update was compiled for Android and both Windows hosts; no additional automated test suite was requested or run for this change.

Android API generation and model detection run on a worker thread because native HTTP response streams may perform synchronous reads. Rendering/progress and approval dialogs are marshalled explicitly to the UI.

The settings menu was opened and visually checked on the Pixel 9 Pro Fold after installation of version 1.79.0. Its corrected tonal card contrast is shown in the device capture (local device capture). This UI check does not validate provider exchanges or every tool end to end.

## Device UI validation (1.79.0)

The final APK was also installed and launched on the authorized Pixel 7 Pro development phone (version 1.79.0, code 113). Manual UI checks covered the settings menu in dark mode, General labels, Skills labels and scrolling, slash suggestions, keyboard dismissal and Android Back from Skills to Settings and then to the chat. Draft text was cleared after the slash check; no API message was sent for these checks.

- Settings menu (local device capture)
- Skills (local device capture)
- Web, files and memory settings (local device capture)
- Slash suggestions with the keyboard (local device capture)

Material switch titles are rendered outside the control template and exposed explicitly to accessibility. On Android 13+, a platform back callback is registered only while a modal or keyboard is open; older Android keeps the legacy handler. This follows the [Android guidance for custom Back navigation](https://developer.android.com/guide/navigation/custom-back/predictive-back-gesture).

Compilation and these UI checks do not confirm complete provider/tool exchanges; no automated test suite was run for this feature update.

## Mobile discussions sidebar (1.80.0)

The toolbar menu opens a left drawer in place of the Project and Discussion dropdowns. It groups chats under collapsible projects, shows the selected discussion, provides a new discussion action for the current project and a separate + action for each project. New project and Settings actions remain in the footer. The project caption remains visible below the header label; since 1.83.0 the label is the model name.

Changing discussions saves and restores the composer text, caret selection and attached images in memory for the current app session. These drafts are not persisted across process termination. Imported text documents remain scoped to their saved conversation. Navigation is disabled while a reply or import is in progress.

The drawer respects Android insets and closes with its close button, Android Back or a tap outside. Version 1.80.0 (code 114) was installed and launched on the authorized Pixel 7 Pro on 2026-10-10. Manual checks confirmed drawer opening, project collapse/expand, Android Back, outside dismissal and access to Settings. The device had no existing chats, so switching between populated chats and draft restoration were reviewed in code but not exercised on the phone. No API message was sent and no additional automated suite was run for this update.

- Chat without the dropdowns (local device capture)
- Expanded projects (local device capture)
- Collapsed projects (local device capture)
- Settings opened from the sidebar (local device capture)

Android packaging and both Windows publications completed successfully. GUI and CLI published executables report version 1.80.0.

## Monolith visual identity (1.81.0)

The user-selected white/cyan monolith artwork replaces the previous neon M in the shared app PNG, GUI/CLI native icons, Electron UI/favicon and native Windows/macOS icon files. Android generates adaptive/density launcher resources from that same PNG with a charcoal background.

Version 1.81.0 (code 115) was compiled, installed and launched on the authorized Pixel 7 Pro on 2026-10-10. The startup capture (local device capture) shows the new artwork; the chat capture (local device capture) confirms the existing DeepSeek provider remains selected after updating. No API exchange was performed for this branding update.

Both Windows GUI/CLI publications succeeded, and their embedded executable icons were inspected. The legacy Electron Windows package was rebuilt with the same artwork and only the current single-file engine; generated application files were refreshed in the existing unpacked output without deleting user data. The macOS ICNS and packaging references were updated, but no macOS build/runtime check was performed on this Windows host. Existing terminal logo expectations were adapted to the new shape and two theme colors; no automated test suite was added or run.


## Mobile permissions and chat rendering (1.82.0)

Paramètres / Autorisations has a global selector with Refuser tout, Demander tout (default) and Autoriser tout. It covers every model tool call, including local reads, web access and memory/file writes. Refuser tout returns an explicit denial without executing the tool; Demander tout asks per call; Autoriser tout skips confirmation. Disabled skills/web/file writes/shared memory and conversation file boundaries continue to apply. Unknown persisted modes fail closed; old settings without a global mode migrate to Demander tout. Manual document import/export and memory editing are user actions rather than AI tool calls.

Tool activities are grouped into one compact expandable bubble per user turn. Its count, running tool/progress indicator and outcomes update during execution. Expanding it shows concise French names and result summaries, including refusals/errors. Names and arguments are recovered from the persisted assistant call records and matched to tool replies by call ID on reload. Raw tool JSON remains in storage/model history; no database migration or replacement of results is needed. The General visibility setting controls presentation, not persistence.

The status/reasoning line sits above the composer. User and assistant bubbles use a native Markdig renderer with theme-aware headings, emphasis, lists/task lists, quotes, safe external links, horizontally scrollable tables and code blocks with Copy. Streaming updates are throttled and completed blocks are reused; malformed partial Markdown falls back to readable text without interrupting generation. Images are links rather than automatically fetched content. Mermaid diagrams, math and arbitrary HTML are not rendered through a WebView; their source remains readable. The desktop renderer and its dependencies stay separate.


Version 1.82.0 (code 116) was compiled with zero warnings/errors, installed over the existing app and launched on the Pixel 7 Pro on 2026-10-10. Manual checks covered the default approval mode and its three visible choices, returning without changing user settings, restored grouping of 16 existing tool calls, expansion of French success/error summaries and collapse. The existing provider and conversation were preserved. Captures: modes (local device capture), expanded summaries (local device capture), compact bubble (local device capture). No additional API request or automated test suite was run; live aggregation, approval execution and the full Markdown syntax range were reviewed in code and compiled, not exercised in a new generation on the phone. Both Windows publications succeeded and their executable ProductVersion is 1.82.0.


## Compact composer and header model selection (1.83.0)

The model selector moves from the composer into the toolbar in place of the conversation title, with the project caption below its name. Conversation titles remain in the discussions drawer and in the menu button's accessible name. The same selector opens the provider/model menu and configuration action.

The composer uses one shared row: a 48 dp + attachment action at the left, a wrapping multiline TextBox in the middle and a 48 dp accented Send action at the right. Empty/short drafts use one line; longer drafts and explicit line breaks grow to the measured wrapped text height up to the viewport height cap and then scroll inside the input. Stop replaces Send in the same column during generation. Attachments and slash suggestions can appear above the shared row; the reasoning/status line remains above the composer. Draft restoration, text/image/document attachments and Android keyboard insets keep their existing behavior.


The composer uses a plain native TextBox inside its Material surface, keeping the field baseline aligned with the two actions. Settings form field styles remain independent.


Version 1.83.0 (code 117) compiled with zero warnings/errors and was installed over the existing app on the Pixel 7 Pro. Manual checks covered the model menu from the header, the + attachment menu, a short draft on one line with the keyboard, automatic growth for a longer wrapped draft and returning to an empty composer. Temporary text was cleared without sending any message. Captures: final portrait (local device capture), model menu (local device capture), attachment menu (local device capture), short input (local device capture), long input (local device capture). The two draft captures precede the final focus color refinement. No automated test suite or API generation was run for this layout change. Final GUI and CLI Windows publications succeeded and both executable ProductVersions are 1.83.0.

## Platform color and signed release (1.84.0)

Android owns a green-accent source image for launcher, adaptive/density and startup icons. The desktop artwork remains cyan; CLI artwork is orange. Regenerate Android previews with `./build/update-branding-assets.ps1 -Target Android` on Windows. The application title remains Monolith and package ID remains `com.monolithharness.portable`.

The public APK is signed with a durable release certificate. Its private keystore and passwords are outside tracked source; the local Windows backup is in ignored `.signing/android/`, with its password protected by Windows DPAPI; GitHub Actions uses the repository's four encrypted signing secrets (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, `ANDROID_STORE_PASSWORD`). `.keystore` / `.jks` files are ignored. Retain and back up the release key: later updates must use the same certificate and an increased application version code. No signing secret or app profile is included in downloads.

USB development builds use a different debug certificate. Android cannot install a release APK over an app signed with a different key. Uninstalling removes private chats, providers, documents and memory; keep the existing debug installation if its data must be preserved. No debug installation is replaced as part of publishing this release.

The manual `Android portable (manual)` workflow accepts a source ref and defaults to `main`. An empty `release_tag` produces downloadable build artifacts; a matching existing `android-vX.Y.Z` or `vX.Y.Z` release tag attaches the signed APK and checksum file after verifying the exact commit. Release signing requires all four secrets. Public release metadata, APK identity and fingerprints are checked before publishing.

Historical device captures are kept locally because they can contain real conversation content. Compilation and past scoped UI checks do not establish every provider/tool exchange, device or Android version; no new automated functional suite was requested for this branding change.
