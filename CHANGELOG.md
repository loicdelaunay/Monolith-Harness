# Changelog

## 1.29.2 - 2026-09-30

- Published GUI and CLI launchers are now named `MonolithHarness.exe` on Windows and `MonolithHarness` on macOS and Linux. Build scripts, clean release packages and update extraction use this name while retaining portable data and internal assembly identities.
- Included the new illuminated M logo and refreshed GUI/CLI screenshots in the README. Download archives retain their existing names for update channel compatibility.
- Versions up to 1.29.1 require a one-time manual transition to the newly named launcher; keep the existing database and resources when replacing the executable.

## 1.29.1 - 2026-09-30

- Renamed the application and GitHub repository to Monolith Harness. GUI titles, CLI output, terminal profiles, documentation and desktop package metadata use the new name. Existing custom application names remain available.
- The default logo now uses an illuminated M with the original cyan, violet and magenta palette. Desktop and executable icons include matching sizes, and the terminal header uses an M monogram in the selected theme's colors.
- Updated GitHub update checks and download validation for the renamed repository. Existing portable data, application identifiers, executable names and release archive names are retained for compatibility.

## 1.29.0 - 2026-09-29

- Auto agents now evaluate delegation before starting a request. Added an Agents settings page with Selective, Balanced, Proactive (default) and Swarm behavior, live explanations and additional coordination instructions.
- Expanded swarm capacity beyond the previous six-agent/eight-step limits. Teams can launch additional waves and descendants with a shared, configurable run budget: 64 agents, 8 parallel model requests, 3 delegation levels and 200 steps per worker by default. Cancellation and repeated-call protection remain active.
- Subagents inherit the conversation's enabled tools, including terminal, browser, desktop, MCP and permitted Git writes, while respecting Plan mode, sandbox boundaries and existing permissions. Shared host tools are serialized to protect interactive resources.
- Long subagent work compacts its context instead of stopping when the context window fills. Descendants appear with their parent path and persist their tool results and transcripts.
- Team sizes, presets, composite models and `/agents` no longer stop at six members. OpenCode workers can use their authorized native tools; native nested delegation remains disabled for application-managed workers because OpenCode descendants cannot be included in the shared budget.

## 1.28.0 - 2026-09-29

- Added optional separate application logos for light and dark themes, with immediate theme-aware selection. Theme lists now show a sun or moon icon.
- Fixed multiple default project folders when the editor uses Windows line endings. Attaching resources to a conversation now preserves project defaults and retains its existing resources.
- Added optional operating-system notifications for completed chats and requests for attention (Windows notification area, macOS notifications, Linux `notify-send`).
- Added German and Spanish interface languages, with flags in the language picker and matching default response languages.
- The model information area now displays thinking activity and a counter after 30 seconds without new model content. Provider-supplied reasoning also appears as a short single-line excerpt.
- Added slash command autocomplete with Tab, keyboard navigation and `/help`. `/agents`, `/plan` and `/goal` configure the current conversation; `/model`, `/thinking`, `/theme`, `/retry`, `/settings` and `/stop` provide shortcuts. Conversation goals persist and apply to subsequent sends.
- Added independent read and write controls under the GIT skill. Disabled capabilities are removed from exposed tools and rejected at execution time, including subagent requests.
- Added a Reset settings page with SQLite compaction and optimization, plus a confirmed reset of project data, conversations, memories, tasks, templates and approvals. Reset retains application settings, providers and source files.
- Added Ctrl+F to search the full chat history, including older pages, with previous/next navigation and highlighted messages.

## 1.28.0 - 2026-09-29

- Added optional separate application logos for light and dark themes, with immediate theme-aware selection. Theme lists now show a sun or moon icon.
- Fixed multiple default project folders when the editor uses Windows line endings. Attaching resources to a conversation now preserves project defaults and retains its existing resources.
- Added optional operating-system notifications for completed chats and requests for attention (Windows notification area, macOS notifications, Linux `notify-send`).
- Added German and Spanish interface languages, with flags in the language picker and matching default response languages.
- The model information area now displays thinking activity and a counter after 30 seconds without new model content. Provider-supplied reasoning also appears as a short single-line excerpt.
- Added slash command autocomplete with Tab, keyboard navigation and `/help`. `/agents`, `/plan` and `/goal` configure the current conversation; `/model`, `/thinking`, `/theme`, `/retry`, `/settings` and `/stop` provide shortcuts. Conversation goals persist and apply to subsequent sends.
- Added independent read and write controls under the GIT skill. Disabled capabilities are removed from exposed tools and rejected at execution time, including subagent requests.
- Added a Reset settings page with SQLite compaction and optimization, plus a confirmed reset of project data, conversations, memories, tasks, templates and approvals. Reset retains application settings, providers and source files.
- Added Ctrl+F to search the full chat history, including older pages, with previous/next navigation and highlighted messages.

## 1.28.0 - 2026-09-29

- Added optional separate application logos for light and dark themes, with immediate theme-aware selection. Theme lists now show a sun or moon icon.
- Fixed multiple default project folders when the editor uses Windows line endings. Attaching resources to a conversation now preserves project defaults and retains its existing resources.
- Added optional operating-system notifications for completed chats and requests for attention (Windows notification area, macOS notifications, Linux `notify-send`).
- Added German and Spanish interface languages, with flags in the language picker and matching default response languages.
- The model information area now displays thinking activity and a counter after 30 seconds without new model content. Provider-supplied reasoning also appears as a short single-line excerpt.
- Added slash command autocomplete with Tab, keyboard navigation and `/help`. `/agents`, `/plan` and `/goal` configure the current conversation; `/model`, `/thinking`, `/theme`, `/retry`, `/settings` and `/stop` provide shortcuts. Conversation goals persist and apply to subsequent sends.
- Added independent read and write controls under the GIT skill. Disabled capabilities are removed from exposed tools and rejected at execution time, including subagent requests.
- Added a Reset settings page with SQLite compaction and optimization, plus a confirmed reset of project data, conversations, memories, tasks, templates and approvals. Reset retains application settings, providers and source files.
- Added Ctrl+F to search the full chat history, including older pages, with previous/next navigation and highlighted messages.

## 1.27.0 - 2026-09-28

- Added the GIT skill with repository status, diffs, history, branches, initialization, explicit file staging/unstaging, commits, and approved fetch/pull/push operations. Pulls only fast-forward; forced pushes and destructive reset/clean operations are not exposed. Plan mode and subagents only receive the Git read tools.
- Added the FILE INDEX skill: create a portable `index.ohm` in an attached folder, browse its immediate children or search paths and descriptions with pagination, and enrich file/folder descriptions from inspected contents.
- Existing indexes update after source-tool edits while FILE INDEX is enabled and reconcile external additions, deletions and renames at their next consultation. Changed model descriptions are marked for review and retained alongside refreshed structural descriptions. Automatic maintenance can be disabled per index; Plan queries never write to disk.
- Both skills are available in the GUI and CLI skill selectors, use the existing source boundaries and permission policy, and start disabled. GUI, CLI and legacy desktop host versions are aligned at 1.27.0.

## 1.26.1 - 2026-09-28

- The memory database viewer now opens in its own resizable window, independent of Settings. Its table, combined filters, row details, pagination and copy action remain available; reopening it focuses the existing window.
- Fixed the sidebar version chip so the full `v1.26.1` label stays visible beside custom application names and still opens the changelog.

## 1.26.0 - 2026-09-28

- Added an Appearance settings tab grouping themes and the three independent font selectors.
- Added Customize under Theme: create named copies of built-in or custom themes, edit 21 individual colors with a color picker or hexadecimal input, preview messages and contrast, and delete custom palettes. Optional colors can follow the base palette automatically. Custom themes are saved in portable settings; Cancel discards the draft.
- Custom palettes are restored at startup and supported by the shared CLI palette. The version chip continues to derive a readable gradient from the active theme.
- Moved GitHub updates and log settings to About, anchored at the bottom of settings navigation. Automatic conversation naming and its model remain in General.
- Aligned the displayed version and update checks with GUI, CLI and desktop host version 1.26.0.

## 1.25.0 - 2026-09-28

- Project folders can stay expanded independently. Opening or closing a folder no longer switches the active conversation, and other expanded projects remain visible when navigating between chats.
- Added Pin/Unpin to conversation context menus. Pinned conversations are saved in SQLite and also appear above Projects while remaining in their original folders.
- Parent project badges now count running chats, unread completed replies and chats needing attention. Pending requests stay marked until resolved; read completion badges clear when opening the chat. These indicators remain available independently of bell and sound preferences.
- Replaced the sidebar version label with a theme-aware gradient chip. Clicking opens the bundled changelog in a scrollable dialog, including when offline.
- Aligned GUI and CLI at version 1.25.0.

## 1.24.0 - 2026-09-28

- Stabilized conversation rows on hover by reserving favorite-button space and ignoring pointer exits between child controls.
- Added three independent font selectors in General settings: interface, user messages/composer and model replies. Installed fonts are listed with previews; code retains its monospaced font.
- Response footers now show the local response time alongside generation duration on hover. New completion timestamps persist in SQLite and are retained by forks; historical timestamps are left unknown.
- Displayed the software version in smaller text beside the sidebar application name.
- View memory now opens a database-style dialog across all projects and conversations, with every memory column, combined column filters, sorting, pagination, full selected-row details and copy. Existing editing remains available separately.
- Aligned GUI, CLI and desktop host versions at 1.24.0.

## 1.23.0 - 2026-09-28

- Restored the application logo and name above the project sidebar. Moved the notification bell to the left of conversation search and replaced the filter glyph with a magnifying glass.
- Added persistent project icons and colors with a preview in Manage project.
- Fixed restoration of vision and naming model dropdowns, including manually entered model names and refreshing the vision catalog without clearing the selection.
- Added an audio icon to Notifications settings and six new sounds: Chime, Glass, Marimba, Digital, Success and Water drop. All nine sounds support volume and preview.
- Published GUI and CLI at version 1.23.0.

## 1.22.0 - 2026-09-28

- Reorganized the desktop conversation sidebar into compact project folders with expandable conversation lists, discreet activity ages, preserved favorites and archives, and a separate section for conversations without a project.
- Added configurable automatic retries in General settings: enable/disable, retry count and delay. Transient model request failures and interrupted streams can retry without replaying completed tools; cancellation stops waiting immediately. OpenCode retries read-only polling and explicitly rate-limited submissions without resubmitting ambiguous remote actions.
- Added a notification bell and conversation badges for completed chats and user actions, with a dedicated Notifications settings page for event selection, sound, tone, volume and preview.
- Display a spinner and “Nommage…” while AI conversation naming is in progress.
- Added per-conversation subagent count and role configuration, automatic count/role toggles, and reusable presets that can be saved or deleted. Configured roles and limits are applied by the shared agent runtime; forks retain the settings.
- Shift+Enter now inserts a line in the composer. Ctrl+wheel changes the text size in the window under the pointer; Ctrl+Shift+wheel changes all application windows. Ctrl+0 and Ctrl+Shift+0 reset the corresponding scope.
- Aligned GUI, CLI and the legacy desktop host versions at 1.22.0.

## 1.21.2 - 2026-09-27

- Benchmark challenge headers now show their total elapsed time after completion, failure or cancellation, including generation and functional checks. JSON entries retain this duration independently from model throughput metrics.
- Benchmark responses now display reasoning streamed by the provider while generation is in progress, followed by the answer. Completed reports retain the full provider reasoning; the latest streamed update is preserved when generation stops.
- Fixed the rich-text editors failing to open on Uno Desktop: local HTML navigation is now accepted for the exact bundled document, while other destinations remain blocked. The same fix applies to the benchmark preview.
- Aligned GUI and CLI versions at 1.21.2, including the archive scrolling fix from 1.21.1.

## 1.21.1 - 2026-09-27

- Fixed the conversation sidebar losing the archive viewport after collapsing and reopening it. Active conversations now use the remaining height while archives keep a bounded, independently scrollable area, including after window resizing.
- Aligned GUI and CLI versions at 1.21.1.

## 1.21.0 — 2026-09-27

- Replaced the former Hard benchmark with six seeded ARC-AGI-2 grid tasks, exact asymmetric tour optimization, planted 3-SAT, Killer Sudoku and three full algorithm regression challenges. Valid witnesses are checked independently; the previous short BBH/HumanEval exercises no longer define Hard.
- Added interactive application coding benchmarks: a 3D Rubik’s Cube with a solver and orbit camera, a 3D gravitational simulator, a visual logic-circuit editor, and an optimal collision-free multi-agent route planner. Run one application, all four, or combine them with reasoning tasks.
- Added a right-hand offline preview for the HTML/CSS/JavaScript produced by the selected model. The app independently checks cube facelets and solutions, numerical integration, circuit outputs, path legality and optimality. Generated applications can be reopened and their HTML copied after the run; visual appearance and usability remain a separate human inspection.
- Added reproducible series numbers and 3/10/20-minute request limits. Reports include selected task IDs, seed, source revisions, individual functional checks and generated HTML. Generated pages run inside a sandboxed iframe with network access blocked by content security policies.
- Updated the benchmark suite to `omh-model-tools-v3-frontier`. Public subsets, transformations and original tasks are documented; these are not official ARC scores or an empirically calibrated frontier leaderboard. GUI and CLI versions are aligned at 1.21.0.

## 1.20.0 — 2026-09-27

- Translator and Proofreader now accept rich email content and copy HTML plus plain text back to the clipboard. Translation and rephrasing retain styled text runs, links, lists, paragraphs and tables; verified spelling corrections preserve the surrounding markup.
- Replaced Proofreader mode buttons with native tabs. Removed routine status text and token/speed counters from both writing tools; retained progress, cancellation and error feedback. Primary actions consistently use an accent button at the bottom right of each tool window.
- Added Easy, Medium and Hard model benchmarks (7, 9 and 12 requests). Medium and Hard include attributed BIG-Bench Hard questions and bug-fixing adaptations of HumanEval tasks, with fixed source revisions and bundled MIT notices.
- Added an S–F success grade before the throughput card, with S for 100% and F for 0%, explicit thresholds and provisional scores on incomplete runs. JSON reports include difficulty, source references and grading details. These selected/adapted tasks are not official BBH or HumanEval scores.
- Aligned GUI and CLI versions at 1.20.0.

## 1.19.0 — 2026-09-27

- Added a **Tools / Outils** dropdown beside Scheduled tasks, opening independent Translator, Proofreader and Model benchmark windows with the application's theme and configured models.
- Translate between source and target languages in side-by-side panels, detect the source language, swap languages and copy results. Proofreading offers verified, individually applicable spelling/grammar/punctuation suggestions, apply all, ignore and rephrasing.
- Benchmark a selected model with one throughput sample, three logic challenges and three bug-fixing challenges. Inspect expected answers, input/output token usage, latency and end-to-end tokens per second, then copy the JSON report. Estimated token counts are labelled explicitly; generated code is not executed.
- Tool requests support cancellation, use isolated provider requests without chat history or agent tools, and preserve the chat's selected model. GUI and CLI versions remain aligned.

## 1.18.0 — 2026-09-26

- Expanded the shared pixel-art skill with palette-based sprite stamps, connected brushes, symmetry, outlined rectangles/ellipses, flood fill, color replacement, and selection copy/move/flip/rotation. Compact region inspection returns reusable patterns to reduce repetitive tool calls.
- Aligned the transparency checker and grid with the actual pixel cells. GUI previews use whole physical pixels, including 125%/150% Windows display scaling, and scroll rather than shrink below one screen pixel per cell. Pixel-art exports reject fractional cell sizes.

## 1.17.1 — 2026-09-26

- Fixed HTTP 400 errors after an assistant requested several image-producing tools together. GUI and CLI now send every tool result before adding the captured images to the next model request.

## 1.17.0 — 2026-09-26

- Added explicit Classic and Pixel art modes to the Asset generator in the GUI and AI tools. Pixel art offers 16 × 16, 32 × 32, 64 × 64, 128 × 128 and custom grid sizes, with one addressable cell per pixel by default.
- Pixel-art previews and captures use nearest-neighbor enlargement; pixel-only editing and crisp SVG output keep drawn edges sharp. Existing grid-based assets remain readable.

## 1.16.0 — 2026-09-25

- Added an opt-in **[BETA] Bypass free limitation** toggle when creating or editing an OpenCode provider (in both GUI and CLI), allowing the use of free models (such as `opencode/big-pickle`, `zen`, or other free-tier models) on another harness without being blocked by free-tier quota or rate limit errors.
- OpenCode requests and runner script now forward `x-opencode-client: desktop` headers, allow cross-origin requests (`cors: ['*']`), and automatically renew exhausted sessions while preserving prior conversation history if a free usage limit error is encountered.

## 1.15.0 — 2026-09-25

- Select a range of conversations with Shift+click (or add individual conversations with Ctrl+click), then right-click the selection to archive or delete them together. The selection count appears in the project sidebar.
- Each project now has a persistent **Archive** subgroup. Archived conversations remain readable and can be restored or deleted; existing conversations remain active after the database migration.

## 1.14.0 — 2026-09-25

- The integrated Web tool now supports separate tabs per conversation. Open, select and close tabs in the GUI or through the browser skill; links that request a new window open a new tab. A failed WebView closes only its tab.
- Asset generator now draws hard-edged pixel art on a configurable logical grid. Numeric alignment guides and optional center/grid overlays help position elements without adding guides to exported artwork.
- Build frame-by-frame animations with individual frame durations and live preview. Export an animated SVG, looping GIF or a ZIP of numbered PNG frames with a timing manifest, in addition to the existing static formats. GUI and CLI share the same asset tools.

## 1.13.0 — 2026-09-25

- Added the opt-in **Asset generator / Générateur d’assets** skill: the AI can create vector canvases, draw shapes, curves and text, arrange layers, inspect its artwork with canvas-only image captures, and refine it incrementally.
- The GUI's **Tools > Assets** panel displays successful drawing updates live, with a color palette, layer visibility and ordering, and export controls. Drawings remain editable and are saved separately for each conversation. The CLI shares the same drawing, capture and export tools.
- Export SVG, PNG, WebP, JPEG or vector PDF, with selectable backgrounds and raster scale. Transparent backgrounds are supported by SVG, PNG and WebP; JPEG requires an opaque background. Drawing operations respect skill permissions and protect concurrent edits with revision checks.

## 1.12.0 — 2026-09-25

- Web research now fetches HTTP(S) pages and APIs directly with an asynchronous .NET client in GUI and CLI, without opening a browser. Supports GET, HEAD, POST, PUT, PATCH, DELETE and OPTIONS, custom headers and UTF-8 request bodies.
- Added per-run HTTP client configuration: timeout, response size limit, redirects, decompression, cookies, user agent and proxy. Responses include status, headers, bounded text or base64 body, truncation and elapsed time.
- The HTTP client is isolated from model-provider credentials and browser sessions. Requests and redirected destinations follow application permissions; Plan and sandbox modes block HTTP tools. Cross-origin redirects strip custom headers and HTTPS downgrades are blocked.

## 1.11.0 — 2026-09-25

- Redesigned the GUI question skill as a focused choice card with clear option descriptions, a dedicated custom-answer choice, collapsible content and step-by-step navigation when several questions are asked.

## 1.10.1 — 2026-09-25

- Browser and DOM access now use the same saved skill selection as other built-in skills. Their choices survive application restarts and stay in sync between Settings, the quick menu and the legacy desktop host.

## 1.10.0 — 2026-09-25

- Added the opt-in **Complete design / Conception complète** skill to both GUI and CLI. It guides projects from focused questions and ideas through technology and visual choices, a living checklist, optional subagent collaboration, implementation, tests and visual verification.
- The workflow adapts to the request and available tools, respects Plan/Execution modes and permissions, and explicitly reports checks that could not be performed.
- The CLI header now shows a symmetrical circular icon in the selected theme's colors and uses the configured application name.

## 1.9.0 — 2026-09-25

- Added a self-contained Linux x64 build for Fedora 44 in both GUI and CLI, with checksummed release archives. The Uno GUI uses X11; desktop-control tools require platform implementations and are unavailable on Linux.
- Added Linux bundled Python and portable key encryption so Fedora provider connections work without a separately installed Python or .NET runtime. The per-installation encryption key stays beside the database and must be kept private with the portable folder.

## 1.8.1 — 2026-09-25

- GUI and CLI downloads now cover Windows x64, macOS Intel and Apple Silicon, with checksums and clean portable archives. macOS builds are unsigned; native interactions still need manual validation.
- Response duration in GUI messages is now shown only while hovering over the message.
- Restored local RAG support on Intel Macs by pinning ONNX Runtime to 1.23.2, which includes native libraries for both Intel and Apple Silicon Macs, as well as Windows.
- Fixed sandbox creation on Windows when a source folder contains an open application database. Excluded SQLite files are now filtered before reading their contents.
- Fixed GitHub Actions downloading only the Git LFS pointer instead of the bundled MiniLM weights. Builds now verify the model checksum before embedding it and explain how to retrieve missing or invalid weights.
- Fixed macOS CI tests failing on symbolic-link ancestors in system temporary paths by using the runner's physical temporary directory; sandbox link restrictions remain enforced. Updated the desktop service test to match the ten available themes.

## 1.8.0 — 2026-09-24

- Added a shared response-style preference in GUI General settings and CLI `/settings`: DEFAULT leaves prompts unchanged; SHORT, PRAGMATIC, DETAILED and FUN guide the length and tone of subsequent answers, including OpenCode sessions.
- Fixed CLI Backspace deleting a word in terminal hosts that send DEL. Both ordinary Backspace encodings now delete one character/grapheme; word deletion requires explicit modifiers or Ctrl+W.
- Moved update, automatic naming and logging settings to the bottom of the GUI General page.
- Translated all Markdown guides under `docs/` into English and moved the full user guide to `docs/guide.md`, updating navigation links.

## 1.7.1 — 2026-09-24

- Fixed CLI keyboard editing in the composer and text dialogs: Ctrl+A, Shift+arrows, Ctrl+arrows, Ctrl+Shift+arrows, Home/End and word deletion now preserve selections and Unicode characters. Modified VT key sequences are decoded instead of being discarded.
- Selected text is highlighted and replaced when typing or pasting. Added undo/redo, clipboard copy/cut/paste on Windows and macOS, and caret-aware rendering in long input fields. Ctrl+C still stops the active run when no text is selected; masked credentials cannot be copied or cut.

## 1.7.0 — 2026-09-24

- CLI fonts can now be selected independently from the color theme, with a separate size setting, a custom installed-font name, and bundled VT323, Share Tech Mono and Space Mono fonts under their original OFL licenses. `/font` exports the font/profile or installs them for the current Windows user; a new terminal tab applies the font.
- Added GitHub updates to GUI Settings > General and CLI `/update`, with separate automatic startup-check preferences. Each interface selects only stable releases for its channel and architecture. Installation downloads and verifies SHA-256, preserves user data, waits for the application to close, replaces only the executable and restarts. The previous executable is retained for recovery.
- Automatic installation is limited to published Windows standalone builds; no installation occurs during an agent run or with unsent drafts. Source builds and other platforms retain manual release downloads.

## 1.6.1 — 2026-09-24

- CLI theme selection now previews colors immediately while browsing or filtering. Enter saves the selected theme; Escape restores the previous palette, including themes passed through `--theme`.
- Updated the Windows CLI publication to include inline slash-command completion introduced in 1.6.0.

## 1.6.0 — 2026-09-24

- Added inline CLI slash-command suggestions, filtered as you type. Use arrow keys to select, Tab/Enter to complete, and Escape to dismiss without interrupting the agent. Suggestions include descriptions and adapt to small terminals.

## 1.5.0 — 2026-09-24

- Simplified the CLI to a compact header, a single-column transcript, a prompt between two separators and unobtrusive model/usage information. Command menus are plain searchable lists with command names and descriptions; conversations remain accessible through `/chats` and Ctrl+O.
- Rebuilt `/connect` as a guided flow: provider type (including DeepSeek, local/compatible APIs and OpenCode), masked credentials, automatic discovery or manual model IDs, default model and final confirmation. Failed discovery offers retry/manual entry. Cancelling leaves no partial connection; provider, visible models and selection are saved together.

## 1.4.0 — 2026-09-24

- Added CLI-specific CRT Green, CRT Amber and Neon Synthwave themes with distinct palettes, retro headings, square/double borders and block cursors. CLI appearance is saved independently of the desktop theme; `/theme` and `--theme` select it.
- Added `/font` to export or explicitly install a dedicated Windows Terminal profile with the theme's font and experimental CRT scanline/glow effect. Other terminals keep their own font while displaying the CLI colors and typography. Profiles are also exported beside the portable database and do not rewrite Windows Terminal's existing settings.

## 1.3.0 — 2026-09-24

- Added Electric dark and Electric light themes based on Midnight Black (#0E0F12) and Electric Cyan (#4CC9F0), available in the desktop app and CLI.
- Fixed text selection, accent label/button contrast, menu and dropdown surfaces, and selected/hovered list colors across all themes. Theme previews update shared control colors and activity glows immediately.
- The CLI now uses the complete theme catalog, including correct light palettes for Ivory and Mist.

## 1.2.0 — 2026-09-24

- Project instructions now load off the UI thread across nested folders without stopping conversations at 2,000 directories. Inaccessible or oversized instruction files are skipped.
- Added pinned favorite conversations, configurable AI naming after the first response, and manual AI naming from the conversation menu or CLI.
- Vision bridge now accepts custom guidance and an optional component/shape breakdown with image-relative bounds and polygons.
- Added portable diagnostic logs with severity filtering, configurable retention (seven days by default), and an off switch. Prompts, response content, keys and tool arguments are excluded.
- Queue actions are inline icon buttons; completed model messages show their duration. Streaming preserves completed Markdown blocks and pauses repainting while reading earlier content; the CLI also keeps the reading position anchored.

## 1.1.0 — 2026-09-24

- Added OhMyHarness CLI, a full-screen terminal workspace with searchable commands and conversations, concurrent streaming chats, model/provider selection, Plan/Execution modes, approvals, agent questions, task lists, subagent inspection, memory, MCP, Git diffs, terminals, attachments, queue/steering and Markdown exports. It reuses the shared agent engine and portable SQLite storage.
- Added non-interactive runs with plain text or JSON events, explicit execution/approval options, and standalone CLI publishing for Windows and macOS.

## 1.0.1 — 2026-09-23

- A standalone executable started in a new folder now creates a fresh portable database instead of silently copying conversations and settings from a previous AppData installation. Legacy browser and OpenCode workspace folders are no longer imported automatically. Existing `database.sqlite` files beside the executable remain untouched and continue to migrate normally.
