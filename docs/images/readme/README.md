# README screenshot notes

Updated on **2026-10-11** using local **1.89.0** builds. The images use synthetic demo content, not personal conversations or provider credentials. Model names, tool results and throughput in these demonstrations are illustrative, not measurements of a live provider.

## Desktop GUI

Captured from the published Windows Uno Desktop executable with a separate database for each built-in diagnostic scenario:

| Image | Screen | Diagnostic scenario |
| --- | --- | --- |
| `chat.png` | Projects, conversation navigation and composer | Conversation workspace |
| `workflows.png` | Subagent presets and Git worktree isolation | Conversation workspace |
| `settings.png` | Searchable settings and provider list | Provider navigation |
| `composer.png` | Source folders, documents and image attachments | Composer presentation |

The workspace, provider-navigation, composer-presentation and responsive diagnostic scenarios passed during this refresh. These are scoped UI checks, not a complete functional suite.

The general GUI scenario stopped at `Initial history rendering was not limited to the recent page.` The older theme scenario stopped at `Theme picker unavailable.` Neither was used to claim full validation; the selected images above come from the successful targeted scenarios. No application code was changed for this documentation refresh.

## Terminal interface

`cli-neon.png` and `cli-crt.png` render the exact ANSI frames returned by the **1.89.0** CLI's built-in offline demo, using Consolas. They show the Neon Synthwave and CRT Green palettes. They are rendered terminal frames rather than captures of a particular terminal host. No API request was made.

```powershell
.\artifacts\CLI\MonolithHarness.exe --render-demo --theme neon-synthwave
.\artifacts\CLI\MonolithHarness.exe --render-demo --theme crt-green
```

## Android

The four `android-*.png` images are unmodified Pixel 7 Pro device captures from the Flutter **1.89.0+125** integration scenario on **2026-10-11**. They were inspected and reused for this refresh. That scenario used an in-memory database, temporary documents and a simulated provider, and its existing log reports a passing run.

| Image | Screen |
| --- | --- |
| `android-chat.png` | Markdown reply, HTML app card and document attachment |
| `android-sidebar.png` | Reorderable projects and conversations |
| `android-permissions.png` | Tool approval policy and independent restrictions |
| `android-html.png` | Isolated fullscreen web app with bundled Tailwind and Chart.js |

The Flutter application is a local source preview with its own package and data. The Android **1.88.1** APK linked in the main README is the separately published Uno application. These captures do not imply that a Flutter release has been published or that desktop/mobile data synchronization is available. See the [Flutter guide](../../flutter-android.md).

The main [README](../../../README.md#take-a-look) keeps one shared note so individual captions can describe the screens. Earlier screenshots not referenced there are retained as historical assets.
