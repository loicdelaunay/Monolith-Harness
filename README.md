<p align="center">
  <img src="src/MonolithHarness.App/Assets/logo-256.png" alt="Monolith Harness logo" width="112">
</p>

<h1 align="center">Monolith Harness</h1>

**Monolith Harness** publishes both interfaces as **MonolithHarness.exe** on Windows or **MonolithHarness** on Linux. Current downloads are **1.45.1**; portable data remains compatible with existing workspaces.

Installations up to 1.29.1 need a one-time manual update to the new launcher before using subsequent automatic updates. Keep your database and resources. See the [update guide](docs/cli.md#github-updates).

<p align="center"><strong>Your AI workspace in one portable executable — GUI or CLI.</strong></p>

<p align="center">
  <a href="https://github.com/loicdelaunay/Monolith-Harness/releases/tag/v1.45.1">Download GUI</a>
  · <a href="https://github.com/loicdelaunay/Monolith-Harness/releases/tag/cli-v1.45.1">Download CLI</a>
  · <a href="#quick-start">Quick start</a>
  · <a href="#build-from-source">Build from source</a>
  · <a href="docs/guide.md">User guide</a>
</p>

**Tired of powerful AI harnesses that need a stack of configuration and external services before the first conversation? What if a portable EXE handled the workspace?**

> **No Monolith Harness account. No telemetry to a Monolith Harness service. No subscription.** Just a standalone app with its workspace beside the EXE. Model requests go to the provider you choose, which may have its own costs.
>
> **Why build it this way?** I needed something straightforward enough to use at work, without a stack of extra services. And I thought it would be nice to share it, too. :)

Monolith Harness brings projects, concurrent chats, agents, sources, tools, and settings into one application. The Windows and Linux downloads ship as self-contained executables. Put one in a writable folder, connect a model provider, and start working. Its SQLite database and application-managed resources live beside the executable, so you can move the workspace by copying the folder after closing the app.

> **Available in GUI and CLI modes.** Choose the desktop workspace or the keyboard-driven terminal experience. Both use the same .NET agent engine, portable storage, projects, and conversations. [GUI download](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/v1.45.1) · [CLI download](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/cli-v1.45.1) · [CLI guide](docs/cli.md)

**Make it yours:** customize the desktop **theme, displayed application name, and logo/icon** in Settings. Keep the custom image beside the executable with a relative path to retain it when moving the folder. The CLI has its own color themes, including green/amber CRT and neon styles, with live previews; terminal fonts and CRT effects use an optional host-terminal profile.

Version **1.45.1** is available as GUI and CLI downloads for Windows x64 and Fedora Linux x64. macOS publications are temporarily paused. Both interfaces include GitHub update checks; the CLI also offers an independent font/size picker with bundled **VT323**, **Share Tech Mono** and **Space Mono**. GUI Settings → About and CLI `/update` can download a verified compatible release and restart while preserving portable data. See the [font and update guide](docs/cli.md#cli-themes-fonts-and-crt).

The chat model itself is **not** bundled: cloud providers need network access and, depending on the service, an API key. Git, Docker/Podman, OpenCode, and Chrome MCP are optional integrations with their own prerequisites. The core app does not require a separate Monolith Harness server.

## Take a look

![Monolith Harness project conversations and chat interface, captured with synthetic demo data](docs/images/readme/chat.png)

<sub>GUI source build 1.29.1, captured with synthetic demo data and the new M logo. Project chats, model choice, response speed, context usage, and the composer stay in view.</sub>

<table>
  <tr>
    <td width="50%">
      <a href="docs/images/readme/workflows.png"><img src="docs/images/readme/workflows.png" alt="Tools and work modes menu" width="100%"></a><br>
      <sub>Attach sources, select Plan or Execution, configure subagents, and toggle skills.</sub>
    </td>
    <td width="50%">
      <a href="docs/images/readme/settings.png"><img src="docs/images/readme/settings.png" alt="Application settings" width="100%"></a><br>
      <sub>Language, themes, providers, permissions, MCP, and more in one settings window.</sub>
    </td>
  </tr>
</table>

### Terminal interface

The CLI offers the same conversation workflow with its own terminal themes. These images are rendered from the built-in offline demo in source build 1.29.1 with synthetic content and the new M logo; no API request was made.

<table>
  <tr>
    <td width="50%">
      <a href="docs/images/readme/cli-neon.png"><img src="docs/images/readme/cli-neon.png" alt="Monolith Harness CLI conversation in Neon Synthwave theme" width="100%"></a><br>
      <sub>Neon Synthwave — conversation, tool activity and composer.</sub>
    </td>
    <td width="50%">
      <a href="docs/images/readme/cli-crt.png"><img src="docs/images/readme/cli-crt.png" alt="Monolith Harness CLI conversation in CRT Green theme" width="100%"></a><br>
      <sub>CRT Green — the same workflow in a retro terminal palette.</sub>
    </td>
  </tr>
</table>

<details>
<summary>See the two-level memory settings</summary>

![Conversation and shared memory settings](docs/images/readme/memory.png)

</details>

## New in the current downloads

**New in 1.45.1:** a compact **Model and thinking** panel with autocomplete, inline thinking selection and model-specific context limits. Automatic context uses the maximum reported by your API when available; refresh models to detect capacities. Context details stay accessible via an information button. Reused tokens appear with reply duration and time, tool bubbles show live progress, and adjacent Markdown sections support continuous text selection.

**New since 1.40.0:** providers **OpenRouter, Groq, Google Gemini, Mistral, Z.ai and NVIDIA NIM**, **Local · Beta** with GGUF import and optional Hugging Face downloads, and an **Image generation · Beta** skill using a selected provider/model. Local model compatibility is estimated from machine characteristics; optional runtimes and models download separately. See [local models and images](docs/local-models-and-images.md), [cloud providers](docs/cloud-providers.md) and the [1.45.1 release guide](docs/releases/v1.45.1.md).

**New in 1.40.0:** **Tools → Preview** opens local images, TXT and Markdown from chat links or compatible attachments in the right workspace pane. Real **Raw / Preview** tabs offer rendered Markdown, image fit/zoom, original decoded text or hexadecimal bytes, copy and refresh. File reads and decoding run in the background, with pagination, size limits and cancellation. See the [Preview guide](docs/file-preview.md).

**New in 1.39.0:** GUI and CLI show **Preloading context** before the first actual model output, with estimated context size and elapsed wait. GUI details distinguish request preparation, sending and waiting; these are client observations rather than a provider prefill percentage.

**New in 1.38.0:** GUI Appearance adds **Compact / Normal / Spacious** chat density. General settings add naming after the first message or response, a switchable attention section, a Windows rendering GPU preference and optional inactivity-based archive/delete policies (disabled initially). Tool results retain execution duration, and Shift+Enter inserts one newline. See [conversation display and activity](docs/conversation-display.md).

**New in 1.37.0:** the translator and proofreader handle formatted paste in batches, preserve email formatting and avoid idle polling and unnecessary redraws. **AI Generated detector** can also use a configured provider and model for text and local documents, with a saved selection and tracked token usage. Model scores are explicitly uncalibrated indicators; SlopTotal remains available for web-page and site fingerprint analysis.

**New in 1.36.0:** all projects, assemblies and embedded resources now use MonolithHarness names. Current GUI and CLI releases target Windows x64 and Linux x64; macOS publication is temporarily suspended. Existing portable data remains compatible.

**Since 1.35.0:** Settings → Compaction controls automatic triggering, context targets, summary strength, custom instructions and recent/full-history/tool-first strategies. GUI, CLI and compatible subagents share the policy; failed compaction preserves the original messages.

**Since 1.34.0:** the CLI has a distinct terminal icon, an animated thinking composer, Up/Down message history and a live activity panel for each subagent.

**Since 1.33.1:** creating a project now opens the same editor as Manage project, including icon, color preview, multiple default folders and optional permission-file import. The first conversation inherits the selected folders immediately.

**Since 1.33.0:** **Tools → AI Generated detector** connects to the real [SlopTotal](https://github.com/pablocaeg/sloptotal) service for text, URLs and documents. Compare live detector results, global and paragraph scores, or website-builder fingerprints. A local or remote service is required; the optional local installer needs Docker and downloads models only when explicitly started. Detection scores are indicators, not proof of authorship.

**Since 1.32.0:** **Tools → Token consumption** shows timelines, model breakdowns and detailed calls, with Today, 3 days, 7 days, 30 days and 1 year filters. GUI and CLI record usage for chats, agents and model tools, distinguishing provider counters from estimates. Filtered details can be copied as CSV.

**Since 1.31.0:** new GUI profiles receive an illustrated theme/provider/skills welcome guide. The chat model and thinking controls adapt to smaller windows, and the context popover explains token use with a chart.

**Since 1.30.0:** local chat paths have opening actions in their context menu, application branding lives in Appearance, custom names can use two compact lines, and Git / File Index have Beta labels and skill details.

**New in 1.29.2:** Monolith Harness now has an illuminated M logo, refreshed GUI and CLI screenshots, and consistently named launchers for every platform: `MonolithHarness.exe` on Windows and `MonolithHarness` on macOS/Linux. Existing portable data is retained; the Windows updater handles the new executable name.

**New in 1.29.0:** Auto evaluates delegation before starting a request. Settings → Agents explains Selective, Balanced, Proactive and Swarm behavior and lets you configure coordination instructions and shared budgets. Teams can launch additional waves and descendants with inherited tool permissions, context compaction and cancellation. Defaults are 64 agents per request, 8 parallel model requests, 3 delegation levels and 200 steps per worker. OpenCode workers retain a restriction on native nested delegation.

**Also included from 1.28.0:** light/dark logos, multiple default project folders, optional OS notifications, German and Spanish languages, thinking activity and a stalled-response counter, slash autocomplete, independent Git read/write permissions, database maintenance and Ctrl+F chat search.

**New in 1.27.0:** Enable the GIT skill to inspect repositories, stage selected files, commit and synchronize branches with the application's permission controls. FILE INDEX creates a searchable `index.ohm` describing files and folders and keeps it current as the project changes. See the [GIT and FILE INDEX guide](docs/git-file-index.md).

**New in 1.26.0:** Appearance groups themes and fonts. Create, edit and delete custom themes with 21 independent colors, live previews and shared GUI/CLI palettes. GitHub updates and logging are in About.

**New in 1.25.0:** Open several project folders at once, pin chats above Projects and see running, completed or attention-needed counts on their parent folders. Click the version chip to read the bundled changelog.

**Since 1.22.0:** compact conversations, configurable retries, completion and attention notifications with sound, saved agent roles and counts, three independent font choices, and a database-style memory viewer. See the [changelog](CHANGELOG.md) for the full list.

**Earlier in 1.21.2:** Benchmark runs gained challenge duration and streamed reasoning. The rich-text translator, proofreader and benchmark preview open on Uno Desktop; the archive remains scrollable after reopening.

**New in 1.21.0:** Hard benchmark challenges include visual app-building tasks with an interactive preview, including a 3D Rubik's Cube. See the [changelog](CHANGELOG.md) for task details and scoring limits.

**Earlier in 1.14.0:** The integrated Web pane now has independent tabs per conversation, also controllable by the AI browser skill. Asset generator adds pixel art, center and grid guides, timed animation frames, and animated SVG, GIF and PNG-sequence exports in GUI and CLI. [Asset generator guide](docs/asset-generator.md).

**Also new in 1.13.0:** The opt-in Asset generator lets the AI draw SVG shapes, colors, text and layers in a live GUI canvas, capture its artwork for visual checks, and export SVG, PNG, WebP, JPEG or PDF from GUI or CLI.

**Also new in 1.12.0:** Web research can now fetch pages and call APIs directly through the built-in .NET HTTP client in both GUI and CLI. Configure timeout, response limits, redirects, cookies, decompression, user agent and proxy without opening a browser. Requests follow application permissions. See the [HTTP tools guide](docs/web-http.md).

**Since 1.9.0:** the complete-design skill helps guide projects from discovery through implementation and testing; browser access choices now persist with other skills; the GUI question tool has a clearer step-by-step choice card; and the CLI logo follows the selected theme. Current GUI and CLI archives are available for Windows x64 and Fedora Linux x64. Linux archives use `.tar.gz` to preserve executable permissions; see the [Fedora guide](docs/fedora.md). The [Mac guide](docs/macos.md) covers historical releases and source builds while publication is paused.

Since the [GUI v1.0.0 release](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/v1.0.0), the source has gained the following updates. **GUI v1.45.1 and CLI v1.45.1** package the same shared engine for Windows x64 and Linux x64.

- **Response styles:** DEFAULT, SHORT, PRAGMATIC, DETAILED and FUN, shared between GUI General settings and CLI `/settings`.
- **CLI editing:** selection, word navigation, copy/cut/paste and undo/redo; ordinary Backspace deletes one character.
- **A portable CLI:** compact chat, concurrent conversations, tools, agent questions, approvals, queue/steering, and plain-text or JSON automation.
- **Faster terminal setup:** guided `/connect` for DeepSeek, OpenAI, compatible/local APIs and OpenCode; automatic or manual model selection; inline `/` completion with arrow keys and Tab/Enter.
- **Personalized appearance:** immediate CLI theme previews, CRT Green, CRT Amber and Neon Synthwave; Electric dark/light palettes and improved theme contrast in the source for both interfaces.
- **Better conversations:** favorites, optional AI titles with a chosen model, response duration, compact queue controls and steadier streaming while reading earlier messages.
- **Stronger project support:** asynchronous discovery of nested project instructions, vision descriptions with custom guidance and component coordinates, and configurable portable logs.
- **Fresh portable workspaces:** launching the executable in a new folder creates fresh data instead of importing previous AppData conversations.
- **Guided work:** Complete design coordinates questions, choices, implementation, optional subagents and verification; the GUI question card presents one decision at a time.

See the [full changelog](CHANGELOG.md) for version-by-version details. All current GUI and CLI downloads include changes through v1.45.1.

## Built-in skills — one click to enable

The built-in skills and their tool implementations are written in **.NET / C# and compiled into the executable**. This keeps the core toolset fast, simple and standalone, with permission checks and Plan/Execution restrictions for controlled access. Enable a skill with **one click** in the GUI, or toggle it through `/skills` in the CLI. Some skills also need a provider or host permission configured before use.

| Skill | What it gives the agent |
| --- | --- |
| **Complete design** | Guide a project from discovery questions and ideas through technology/visual choices, optional subagents, implementation, tests and visual verification. |
| **Source exploration** | List and read project files, including focused line ranges. |
| **GIT** | Inspect repositories, stage selected files, commit and synchronize branches with approvals. See [GIT and FILE INDEX](docs/git-file-index.md). |
| **FILE INDEX** | Create a portable `index.ohm` with file/folder descriptions, search and browse it, and keep it current as files change. See [GIT and FILE INDEX](docs/git-file-index.md). |
| **Source editing** | Create, write and edit files in attached sources. |
| **Code search glob/grep** | Find files and text with line numbers. |
| **Multi-file patch with diff** | Preview and apply changes across multiple files. |
| **Terminal** | Run commands concurrently, manage terminal sessions and await results. |
| **Python scripts** | Create and execute scripts with the bundled Python runtime. |
| **Semantic search RAG** | Index and search sources using local multilingual embeddings or an API. |
| **Memory · Conversation** | Search and save facts scoped to the current conversation. |
| **Memory · Shared** | Reuse project, general and user knowledge across conversations. |
| **Bypass image AI** | Ask a dedicated vision model to describe images or identify components and coordinates. |
| **Asset generator** | Draw vector or pixel-art assets on an editable layered canvas, align with guides, animate timed frames, and export SVG, animated SVG, GIF, PNG sequences or static images. See [Asset generator](docs/asset-generator.md). |
| **Web research** | Fetch pages/APIs directly with a configurable .NET HTTP client, without a browser. Optional browser/DOM skills add rendered-page reading and interaction. See [HTTP tools](docs/web-http.md). |
| **AI browser access / DOM access** | Persist the embedded browser's AI access and interaction choices with the other skills. |
| **Application management** | List open application windows and their position and size. |
| **Mouse control** | Click, scroll or drag using screen, window or browser coordinates. |
| **Keyboard control** | Send text and key combinations to the supported desktop/browser host. |
| **Screenshots** | Capture the desktop, a specific application window or a browser page. |
| **Code review** | Guide the agent through reviewing changes and identifying issues. |
| **Planning** | Structure the work before execution. |
| **Summarization** | Produce concise summaries of useful context. |
| **Automatic skill creation** | Save reusable `SKILL.md` procedures globally or under `.omh-ai/skills` in a project. |

Custom `SKILL.md` procedures extend these compiled tools. Browser/desktop control depends on the GUI host; the CLI can use browser automation through MCP and filters unavailable desktop tools. Optional integrations keep their own prerequisites. Enabling a skill does not override permissions or provide a security sandbox; [container sandboxing](docs/sandbox.md) is a separate option.

## What is inside

| Area | Capabilities |
| --- | --- |
| **Models and providers** | Add as many OpenAI-compatible v1 or DeepSeek connections as you need, select their available models, connect OpenCode, or build a composed model with an orchestrator and specialized subagents. |
| **Projects and conversations** | Attach source folders to a project, run several chats at once, queue or steer messages while an agent works, fork or resume from an earlier message, and export a conversation to Markdown. |
| **Agent tools** | An integrated browser with controlled DOM and JavaScript access, multiple asynchronous terminals, source search and editing, a read-only Git diff viewer, file navigation, screenshots, mouse and keyboard controls, and bundled Python for scripts. |
| **Control and extensions** | Per-conversation Plan and Execution modes, optional container sandbox, scoped approval dialogs, configurable skills, project instructions from AGENTS.md, custom SKILL.md files, and MCP servers. |
| **Context that lasts** | Live token and speed indicators, context compaction, conversation and shared memory, semantic search with a bundled multilingual embedding model or an OpenAI-compatible embedding API, and an optional vision-model bridge. |
| **Desktop workflow** | Scheduled project tasks, structured agent checklists and questions, English/French UI, light/dark themes, custom app name and logo, and keyboard-friendly chat. |

Plan mode blocks modifying tools at the application boundary. The optional sandbox uses Docker or Podman and keeps a copy of approved sources separate from the real project until changes are reviewed. Permissions still apply to sensitive actions. See the [agent modes](docs/agent-modes.md) and [sandbox guide](docs/sandbox.md) for their exact boundaries.

## Quick start

1. Download the [GUI release](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/v1.45.1), or choose the [CLI release](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/cli-v1.45.1) for a terminal workspace. The 1.45.1 archives cover Windows x64 and Fedora Linux x64.
2. Extract the archive into a **writable folder** and run <code>MonolithHarness.exe</code> on Windows or <code>./MonolithHarness</code> on Linux. The app creates its skills folder beside the executable.
3. Open **Settings → Providers**. Add a provider and its API key or endpoint. **Test connection** detects, selects, and saves its models; you can change that selection later.
4. Create a project, attach the source folders you want to share with its chats, and start a conversation.

For the CLI, put `MonolithHarness.exe` (Windows) or `MonolithHarness` (Linux) in a writable folder, launch it from your project directory, and enter `/connect`. GUI and CLI have separate downloads. Type `/` to discover commands; use ↑/↓ and Tab to complete them. No .NET installation is required for the published executables.

The integrated browser uses the system Web engine: Edge WebView2 on Windows, WebKit on macOS and WebKitGTK on Linux. Fedora GUI needs X11 and GTK3/WebKitGTK; see the [Fedora guide](docs/fedora.md). Git, container sandboxing, OpenCode, and Chrome MCP only need installation when you enable those integrations. Scheduled tasks run while Monolith Harness is open; they do not wake a sleeping computer.

## Portable data and privacy

Monolith Harness stores <code>database.sqlite</code>, <code>skills/</code>, <code>MCP.json</code>, browser profiles, and other application-managed resources beside the executable. In a fresh folder, it creates a new database with the required schema and starter settings, without importing old conversations from AppData. EF Core applies database migrations on startup. Do not place the app in a read-only directory such as <code>Program Files</code>. Close all instances before copying the folder to another machine.

API keys use Windows DPAPI, the macOS Keychain or, on Linux, AES-GCM with an owner-only `.linux-key` file beside SQLite. On Linux, keep the entire portable folder private and copy the key with the database; anyone who can read both can recover the API keys. Windows/macOS keys are tied to the OS account and need re-entry when moving accounts or systems. The rest of SQLite is **not encrypted**. Only content used for a request is sent to its selected model provider; attaching a source folder does not upload the whole folder automatically.

The bundled RAG embedding model runs locally and supports French and English. Its model file is part of the app release. Source clones use **Git LFS** to retrieve that file.

## Platforms and building

### Terminal workspace

Monolith Harness also includes a [modern CLI](docs/cli.md) with a responsive full-screen interface, searchable commands, concurrent chats, approvals, agent questions, tools and streaming context indicators. It uses the same engine and SQLite data as the desktop app.

~~~powershell
dotnet run --project src/MonolithHarness.Cli -- "E:\Projects\MyProject"
.\publish-cli.ps1 -OutputDirectory artifacts\CLI
# Then: artifacts\CLI\MonolithHarness.exe
~~~

Use <code>MonolithHarness run "Review this project" --project . --json</code> for automation. <code>/connect</code> configures a provider, Ctrl+P opens the command palette, and <code>--database</code> selects an existing desktop workspace.

### Desktop application

The shared application is built with **Uno Platform and .NET 10**. The published Windows x64 release uses Uno Desktop; a native WinUI 3 target is also available. Automated publications currently target Windows x64 and Fedora Linux x64. macOS publication is temporarily paused; platform code and historical downloads remain available.

For a Windows source build, install the .NET 10 SDK and Git LFS. The native WinUI target additionally needs the Windows/WinUI development tools.

~~~powershell
git clone https://github.com/loicdelaunay/Monolith-Harness.git
cd Monolith-Harness
git lfs pull
dotnet run --project tests/MonolithHarness.Tests -c Release
.\publish.ps1 -OutputDirectory artifacts\GUI
~~~

Publications use `artifacts/GUI` and `artifacts/CLI`. Temporary test publications belong under `artifacts/TEMP` and should be removed after testing. Add `-NativeWinUI` to the GUI publish command to select the native WinUI target. On a Mac with the .NET 10 SDK and Xcode command-line tools, use <code>bash ./publish-macos.sh arm64</code> or <code>x64</code>; signing and notarization are separate steps.

The [Windows v1.0.0 release](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/v1.0.0) passed 473 offline .NET checks and an application UI smoke scenario. The macOS target is not included in that runtime validation.

## More documentation

- [Dedicated tools](docs/model-tools.md): model-powered translator, proofreader and benchmark, available from **Tools** beside **Scheduled tasks** in the GUI.

Detailed guides: [browser, RAG, and subagents](docs/browser-rag-agents.md), [memory](docs/memory.md), [custom skills](docs/skill-authoring.md), [MCP](docs/mcp.md), [scheduled tasks and models](docs/uno-tasks-models.md), [macOS](docs/macos.md), and the [full user guide](docs/guide.md).

Monolith Harness is available under the [MIT license](LICENSE).
