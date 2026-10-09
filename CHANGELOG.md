# Changelog

## 1.77.0 - 2026-10-09

- Adds the configurable Information skill, available as /informations in Agent and Chat modes: current date/time, computer or UTC clock, timezone, OS/shell, language/region, CPU/runtime memory, application/.NET/bundled Python versions, local PATH tool detection, selected development environment variables, optional paths/identity, conversation context and custom notes. Sensitive optional categories remain off by default; no commands are executed and no arbitrary environment variables are dumped.
- Adds per-category controls and a local preview in Settings → Skills → Information, plus equivalent CLI preferences. The skill injects fresh selected context on each user turn when automatic context is enabled; compatible direct models and subagents can refresh selected categories with get_information. External OpenCode/ACP agents receive snapshots without claiming this application tool exists in their host.

## 1.76.0 - 2026-10-09

- Adds an About setting for automatic update checks: Never, every hour (default), every day or every week. Checks run at startup and periodically while the app is open, respect disabled automatic updates and preserve manual checks.
- Allows manual update installation with unsent conversation drafts and attachments, saving them locally and restoring them after restarting. Running agents still block installation; automatic installation continues to wait for drafts.
- Completes slash skills with a space for continued typing, deferring native editor replacement until the completion key has finished so Enter does not introduce an extra newline or submit the message.

## 1.75.1 - 2026-10-09

- Makes attachment previews shrink with available composer width and window height, including their image, placeholder and removal control. Keeps previews square under text zoom, restores their size when widening and retains horizontal scrolling for many attachments.
- Shows “Voir pièces jointes (X)” next to the expand arrow when attachments are collapsed, with a current count and matching accessible label; expanded previews keep the arrow-only layout.

## 1.75.0 - 2026-10-09

- Makes the composer footer adapt to measured available width: hides token speed first, then the context percentage while retaining its ring, then compacts mode/permission labels and scales controls down only when needed. Preserves saved visibility preferences, restores details when widening, retains tooltips and keeps all controls on one row.
- Uses icon-only Export, Tools and Auto actions when header space is limited, preserving tooltips, accessible labels and the Auto state.
- Restores dragging on recycled sidebar containers without duplicating handlers, refreshes ordering after native drag completion, and keeps the current chat/draft on same-project reorders. Adds a thin accent insertion marker above/below chat and project targets, including moving a chat into a project.

## 1.74.0 - 2026-10-09

- Replaces the proposal ContentDialog with a separate native review window: files on the left, selected-file change blocks with line numbers on the right, green additions and red previous/removed lines. Keeps large diffs virtualized and follows the application theme without WebView. Uses content-sized row containers so colored code lines are contiguous while real blank lines and taller block-action headers retain their size.
- Adds accept/reject actions for each block, each file and the entire batch. Finish is enabled only after every change has a decision; it applies accepted blocks, preserves rejected ranges, and clears the completed proposal. Close preserves pending proposals and saved decisions without applying project changes.
- Shows a prominent full-width review card in the conversation with proposed-file counts and green/red added/removed line totals. Retains stale-revision, source-boundary, changed-file, original-encoding and rollback protections; generations starting during a final file commit wait for it to finish. The CLI retains its file-by-file workflow with improved multi-block differences.

## 1.73.1 - 2026-10-08

- Fixes slash-skill completion inside sentences and at the caret: suggestions no longer require a slash at the start of the draft. Clicking a skill, Tab or Enter completes only its token, preserves surrounding text and never submits the message. The explicit skill instruction is added only when the complete draft is sent.
- Restores skill suggestions in Chat mode with Agent-mode/disabled indications. Coalesces text and selection updates so a newly typed slash or a paste uses the final caret location. Skill requests are also recognized before sentence punctuation.
- Replaces the quick-level slider's thin track and round knob with native, Material 3-inspired split rounded tracks, a narrow vertical handle, discrete level indicators and hover/pressed/focus states. Keeps keyboard/range accessibility, application theme colors, direct level selection and the existing compact popup; fixes contrast of the three-dot view toggle in its selected state.

## 1.73.0 - 2026-10-08

- Adds a per-conversation permission-menu toggle for working outside attached files/folders, enabled by default to preserve existing behavior. Turning it off blocks outside file approval fallbacks and tools that cannot enforce this boundary (local commands/Python, GIT, browser, desktop, MCP and external agents); isolated sandbox commands remain available. Subagents inherit the boundary, use attached sources directly and cannot create external Git worktrees. Saved approval grants and Allow all do not override the resource boundary. Manual user tools remain available.
- Saves the boundary in SQLite, carries it into captured runs and conversation branches, exports it and exposes it through the shared hosting API. The boundary can be changed while the conversation is idle.
- Replaces the Simple/Advanced model-picker flag with a three-dot icon while keeping the existing view toggle and accessibility labels.

## 1.72.1 - 2026-10-08

- Fixes Unknown portable resource folder when loading conversations or preparing file-change proposals: registers proposals in the portable workspace layout and scopes storage to the explicitly selected database instead of the global host directory.
- Adds 31 isolated regression checks for empty reads, path validation, database/chat isolation, persisted proposals, selective review, changed-file protection and lossless/idempotent migration, including directory collisions. Runs them in the portable-storage group and default test suite; updates an obsolete composer-settings assertion so the test project compiles with current settings.

## 1.72.0 - 2026-10-08

- Restores a single compact composer row: removes the permanently visible quick-level slider and centers model, thinking, metrics, mode and Send/Stop controls together, without reserving an extra line under the model.
- Adds a compact quick-level popup inspired by effort pickers, showing the level name, model and a stepped slider. A mode icon in the popup switches between quick levels and detailed model/thinking selection, remembering the selected view. Quick choices apply directly while keeping the popup open; no separate Apply action is required.

## 1.71.0 - 2026-10-08

- Merges quick AI levels into the composer model selector: a compact slider below the model name applies the configured model, thinking effort and Chat/Agent mode directly. The model-name popup retains Simple/Advanced selection. Changes commit after dragging ends or a short keyboard debounce, and unavailable levels are rejected without altering the current configuration.
- Removes the attachment section heading and places its collapse arrow before the first square preview in the same row, reducing composer height while preserving per-conversation folding, attachment previews and accessible descriptions.

## 1.70.0 - 2026-10-08

- Replaces the proofreader and translator WebView editors with native text controls and an optional native formatting preview. HTML clipboard import, transformed text runs and formatted copy remain local and preserve document formatting. Manual edits that change rich document structure revert to plain text with an explicit notice.
- Adds Settings / General / Allow WebView previews in tools, disabled by default. Text tools always use native controls. Interactive application benchmarks require explicit opt-in; saved HTML can be inspected as native source without executing it. This setting does not change the conversation's Web browser or Mermaid/math rendering preferences.
- Adds AngleSharp 1.8.4 solely for bounded local HTML parsing; the text tools do not execute JavaScript, load resources or navigate documents. Removes browser-script synchronization from the native editors.
- Adds a dated NuGet dependency inventory and available-version report, with explicit notes on coordinated graphics updates and the ONNX macOS x64 compatibility pin.

## 1.69.0 - 2026-10-08

- Adds persistent drag-and-drop ordering of sidebar projects and conversations, with conversation moves between projects and a start-discussion button in the empty chat view. Running conversations cannot be moved to another project.
- Adds Settings / Test with cancellable simple checks for naming, RAG embeddings and command approval, and complete checks covering configured provider models. Checks report individual results and never execute the sample command or project tools.
- Autocompletes built-in and project skills from slash names and adds an explicit skill request to the model instructions, including CLI messages. Disabled skills still require enabling and permission rules remain enforced.
- Uses the application theme and palette for Mermaid and includes them in diagram cache keys, including live theme changes.
- Preserves terminal preview scrolling while output updates and keeps the composer footer on one row with compact controls for narrow windows.
- Adds detailed CLI command approval with the command, risk category, validator explanation, working directory, shell and timeout. More information can request an impact explanation from the current model before returning to approval.
- Adds Plan / Proposition / Execution modes. Proposition stores text-file drafts for explicit per-file human review before applying selected files; it rejects stale reviews and changed source files. External agent engines remain in their read-only planning mode and can return textual diffs.
- Documents possible Windows focus freezes involving secondary WebView2 editors and the current limits of error notifications, without claiming a confirmed root cause.

## 1.68.0 - 2026-10-07

- Places the exact copyable command first in the additional permission dialog, followed by a categorical risk bar: green for a valid low-risk decision, orange for review or missing analysis, and red for a risky classification. Removes the warning sentence, keeps validator identity and unavailable-analysis details, and preserves the human approval actions.
- Adds an enabled-by-default Virtualize chat display toggle in Settings / General. Disabling it realizes and retains all visible conversation turns; enabling it resumes viewport-based rendering. Changes apply to the current chat, with selection and search preservation.
- Shows composer token speed using only the last two seconds of received generation output, scoped to each chat and reset per model call/retry. Idle time contributes zero throughput; provider counters and text estimates retain their distinction. Existing response and conversation statistics in the speed tooltip remain unchanged.
- Adds a shield, Checking label and animated circular indicator beside the activity chip while Automatic reviews a command. Tracks queued, preparation, analysis and decision-check stages and elapsed time; no completion percentage is invented when the validator does not supply one. Background chat checks remain scoped to their chat.
- Keeps the activity glow continuous across streamed status updates and uses smoothly eased keyframes instead of restarting its pulse per token. Uses continuous spinner rotation with correct visibility and lifetime cleanup.
- Adds a compact attachment/resource header with a count and per-chat collapse state. Collapsing previews preserves attached content and releases composer space.
- Highlights Ctrl+F matches inside native Markdown text and embedded diagram/math views, preserving formatting, links and diagram DOM. Applies highlights to newly realized turns and streamed answers; closing or clearing search removes them.

## 1.67.0 - 2026-10-07

- Moves LANCET setup into a dedicated Command validation section in Settings / Permissions. Download and prepare the pinned local model explicitly, or import a complete verified 0.4.3 bundle. A local wheels directory supports offline CPU dependency installation; selecting Automatic no longer opens an installation dialog.
- Allows any configured local or API chat model to validate commands instead of LANCET. Shows provider transmission and billing information; supplies no execution tools and requires a bounded structured decision tied to the exact command. Uncertain, invalid or unavailable analysis requires human approval. GUI and CLI share the validator settings.
- Restores Allow all as an explicit permission choice, displayed with an orange warning icon in the GUI. Accepts permission requests without model validation while preserving explicit project Ask and Deny rules; Automatic remains a separate validated mode.

## 1.66.0 - 2026-10-07

- Automatic permissions now analyze each supported terminal command locally with LANCET Nano. First selection opens an installation dialog with an explanation, download progress, verified pinned files and CPU runtime preparation; the mode is enabled only after successful setup. The prepared model stays loaded for subsequent commands.
- Risky, uncertain, oversized or unsupported commands and unavailable analysis request one-time human approval. Saved directory grants cannot bypass this validation; explicit project Ask and Deny rules remain authoritative. External engines without exact command metadata and Python scripts require human review rather than an inferred approval.
- Command confirmation adds More info using the current conversation model to explain impact without execution tools, with an explicit notice about sending the command to its provider. The shared engine and CLI enforce the same command validation; the CLI also offers first-use installation. Windows, Linux and macOS use the bundled Python CPU runtime; unsupported shells remain subject to manual confirmation.

- Improves the additional permission dialog with a separate copyable command, working directory and shell/timeout metadata, plus incrementally rendered Markdown impact explanations. Centers composer model, thinking and token/context indicators vertically.

## 1.65.0 - 2026-10-07

- Adds a compact shield and current approval mode beside the composer settings button in Chat and Agent. Choose Ask (default), Automatic or Deny all, with descriptions and a selected-mode marker, or open Settings / Permissions directly. Shares the existing global setting and preserves saved grants and project rules. Adapts the footer to the extra control on narrow windows.

## 1.64.0 - 2026-10-07

- Splits the composer display preference into independent Show token speed and Show context usage toggles in Settings / General, both enabled by default. Migrates the former combined preference and releases only the space used by the hidden indicator.
- Coordinates context and speed hover popups so moving directly between their indicators immediately replaces the popup, including when its surface intercepts pointer events. Hides a popup when its indicator is disabled.
- Keeps the assistant answer bubble hidden during model preparation, sending, reasoning-only output and retries. Shows streamed answer text when it arrives, while reasoning and tools remain in their activity group; preserves errors and interrupted responses. Applies to the native model path and OpenCode.

## 1.63.0 - 2026-10-07

- Replaces the More information panel above the composer with a compact model button in the bottom-right action bar. Shows the actual model name and a thinking indicator; clicking opens the existing Simple/Advanced picker, including model search, thinking and per-model context settings.
- Moves live token speed and the context usage ring into the same action bar. Adds an enabled-by-default Show token speed and context toggle in Settings / General; hiding the indicators preserves generation statistics.
- Preserves quick AI levels and resource thumbnails, and adapts the composer footer to narrower windows without overlapping typing, mode controls or Stop/Send.

## 1.62.0 - 2026-10-07

- Shows update preparation, download progress and installation status in a progress bar and label directly below the sidebar update button, leaving conversation status available for chat activity.
- Sizes user message bubbles to their content and aligns them to the right, with responsive width limits for longer messages. Message actions appear without shifting the bubble.
- Keeps conversation resources and pending attachments visible above the composer as square previews, including image thumbnails, fullscreen viewing with zoom, and removal on hover. Removing a reference preserves its file; the automatic workspace preview can be hidden.
- Adds a compact quick AI level selector immediately to the left of Stop and Send when multiple shortcut levels are configured. Applies the saved model, thinking and Chat/Agent combination while information is collapsed, with a responsive footer on narrow windows.

## 1.61.0 - 2026-10-06

- Adds Settings / Shortcuts with quick model and thinking levels, disabled by default. Configure up to 10 ordered, optionally named combinations of provider/model, thinking effort and Chat or Agent mode, including adding, deleting and reordering levels.
- Adds Simple and Advanced selection when shortcuts are enabled. Simple previews levels with a stepped slider and applies the whole combination together; manual combinations show Custom, and unavailable models are identified without silently switching providers.
- Preserves the existing model search, autocomplete and context controls in Advanced mode. Separates model and thinking into two dropdowns when the composer has enough width, and keeps a combined menu on narrower windows.
- Persists levels and the selected presentation in the portable profile, preserves independent Chat and Agent thinking preferences, and blocks conflicting edits and sends while a level is being applied.

## 1.60.0 - 2026-10-06

- Adds a target-version dropdown above automatic updates in Settings / About: Latest follows new GUI releases; a fixed version keeps update checks and automatic installation on the selected release.
- Loads available stable Windows releases from GitHub without blocking the settings window, sorted newest first and filtered by architecture and verified download metadata. Preserves a selected version when the list cannot be loaded or the release becomes unavailable.
- Refreshes pending updates when the saved target changes, cancels obsolete checks and downloads, and rechecks the selected version before installation. Manual installation requires saving the version choice first; selecting an older release allows returning to that version.

## 1.59.0 - 2026-10-06

- Caches Mermaid SVGs by diagram source and theme in bounded memory, shared between recycled GUI Markdown views. Reuses in-flight renders, isolates SVG IDs, and coalesces streamed diagram updates.
- Preserves unchanged Markdown DOM and diagrams on size changes, suppresses duplicate paint requests, and batches height notifications to prevent repeated Mermaid redraws.
- Virtualizes historical conversation turns: only the viewport and a nearby reading margin retain rich bubbles, decoded attachments and WebViews. Keeps measured placeholder heights, anchors scrolling when earlier content changes size, and recreates offscreen content on demand.
- Preserves live replies, focused content, text selections and activity expansion while recycling history; conversation search explicitly realizes and reveals matching messages. The legacy Electron interface also skips layout and painting for distant bubbles.

## 1.58.0 - 2026-10-06

- Adds enabled-by-default general settings for Mermaid diagrams and mathematical notation, applied independently to Markdown messages and file previews. Disabling rendering keeps source text readable.
- Adds the conversation-level Git worktree switch, enabled by default and included in agent presets. Coding subagents in detected repositories receive independent branches and folders; all project-aware source, terminal and native OpenCode calls follow the isolated scope.
- Automatically commits and integrates successful subagent work through a private integration checkout, then a conflict-free fast-forward into the original branch. Dirty or changed parents and merge conflicts retain the child branch/worktree for review, without altering the parent index or discarding local edits. Worktrees start from committed HEAD; sandbox and Plan sessions retain their existing behavior.

## 1.57.1 - 2026-10-06

- Fixes the Windows WinUI target by composing the speed statistics card inside a Border instead of inheriting the sealed native control.
- Updates HTTP regression coverage to select Legacy for inline response assertions and validate the complete Smart request, settings, search and read tool catalog. Subagent coverage also recognizes its scoped task-reporting tool; naming coverage explicitly selects first-message timing.

## 1.57.0 - 2026-10-05

- Adds Antigravity ACP and ChatGPT Plus / Pro through Codex to the provider settings, welcome flow and CLI connection wizard. ChatGPT uses account sign-in and the plan’s Codex allowance, without an API key or fallback to API billing.
- Adds ACP v1 streaming, reasoning and tool activity, native model discovery, supported effort selection, cancellation and local history replay. ACP permission requests use the application’s approval flow; Chat and Plan select Codex read-only mode.
- Keeps Antigravity permissions enabled. Its current adapter requires Agent / Execute mode and exposes no model catalog; its permissions remain managed by Antigravity. Node.js and authenticated agent installations are described in the connection settings. Native agent tools replace the application’s local tool and subagent orchestration protocols for ACP connections.

## 1.56.0 - 2026-10-05

- Improves Markdown readability with paragraph and section spacing, a clearer line height, and embedded offline LaTeX/KaTeX and Mermaid rendering in chat and file previews. Chat and Agent prompts explain the supported display formats; incomplete diagrams retain their source.
- Groups reasoning, intermediate replies and tool activity into one expandable bubble per user turn. Its collapsed summary follows the latest activity; Show reasoning details controls its initial state while manual choices remain effective during streaming. The final answer stays directly visible.
- Adds persistent up/down controls for queued messages in the GUI, CLI and legacy desktop host, preserving content, attachments, provider routing and steering priority.
- Routes asset captures through the configured Bypass image AI observer when enabled, including with a vision-capable main model. Ordinary images retain direct vision where supported.

## 1.55.1 - 2026-10-03

- Restores the thinking selector in Chat mode: Auto, Low, Medium, High and Disabled can be selected according to model support. Chat starts with Disabled and stores its preference separately from Agent across GUI, CLI and the legacy desktop host. Existing installations initialize the Chat preference to Disabled.
- Applies reasoning-off request enforcement only when Disabled is selected. Chat prompts and OpenCode variants now follow the selected level; unavailable OpenCode levels report an explanation. Conversation exports reflect the active mode preference.

## 1.55.0 - 2026-10-03

- Replaces the width-dependent model information heading with a stable More information label, removing its resize feedback and rapid flickering in both expanded and collapsed states. Collapsed context and speed metrics retain their existing height.
- Moves the existing tool tabs into the panel header, alongside maximize/close actions. Web, Terminal, Git, Files, Assets and Preview use one compact toolbar per page with information and accessible action icons; asset guides, grid and frame editing remain available in an options flyout. Narrow action areas scroll horizontally instead of wrapping.
- Adds Desktop/Mobile responsive display to the Web toolbar and a browser_viewport model tool. Mobile uses a 390-pixel-wide embedded view, desktop fills the panel, and switching preserves the current page. Choices are isolated per native browser tab and reset with the browser session; the tool reports the actual viewport. This is responsive layout preview, not mobile OS, touch or user-agent emulation. Browser skill, Chat restrictions and Chrome MCP routing remain enforced. The legacy desktop browser follows the same display controls.
- Makes Complete design proactive for moderately complex tasks: clarify missing audience, visual direction, platform and technology choices; define acceptance criteria, implement, verify and deliver a complete usable result. Existing answers and explicit user limits are respected; unperformed checks are reported clearly.

## 1.54.1 - 2026-10-03

- Chat requests now require reasoning to be disabled across GUI, CLI and the legacy desktop host. The reasoning selector is locked off in Chat while preserving the Agent preference. Provider-specific off controls are enforced after compatibility transforms; unsupported models produce an explanation instead of silently reverting to automatic reasoning. OpenCode uses an explicitly disabled variant or a declared non-reasoning model.
- Places the settings button inside one compact Chat / Agent capsule. The model information header shows the active model name when it fits, preserving the single-line header and collapsed metrics height.

## 1.54.0 - 2026-10-03

- Moves composer configuration to the right of the Chat / Agent switch and replaces its plus icon with a settings icon. Model, speed and context now uses the sidebar background; its collapsed header shows a live context ring, percentage and tokens per second aligned to the right within the existing header height. Values and estimates stay synchronized with the expanded panel, including conversation switches. GUI and legacy desktop layouts are aligned.
- Preserves useful provider error details in GUI and CLI, including upstream messages, provider names/codes, quota reasons, rate-limit counters and suggested Retry-After delays when available. Selected fields are bounded and credentials are redacted; retry behavior remains unchanged.

## 1.53.0 - 2026-10-03

- Adds two optional Chat skills under the GUI + menu: Web research enabled by default and Python script execution disabled by default. Choices are persisted per conversation and copied into branches, independently of Agent skills. Chat exposes only the enabled HTTP research and bundled-Python tools; permissions still apply, other project/terminal/MCP/subagent tools remain unavailable. Smart HTTP results preserve bounded search/read behavior and their tool exchanges are retained correctly between model rounds. Python uses the conversation scripts directory without attaching project folders. The CLI exposes the same choices through /chat-skills.
- Replaces the separate Chat/Agent buttons with an actual styled switch, rounded housing, mode icons and accessible labels. OpenCode Chat allows only its configured native webfetch/websearch tools after authorization and keeps other native tools denied; the Monolith Python skill is visibly unavailable for OpenCode connections. GUI, CLI and legacy desktop versions remain aligned.

## 1.52.0 - 2026-10-03

- Adds a Chat / Agent selector beside the GUI attachment button, with Agent enabled by default and the choice saved per conversation. Chat uses the selected model and reasoning level with a conversational prompt, images and explicitly attached documents. It exposes no model tools, MCP, project-folder access, autonomous goals or subagent execution; unexpected tool calls and native OpenCode tools are denied by the shared engine. Saved Agent plan/execution, team and sandbox settings remain available when switching back. Stored history is preserved; historical tool envelopes are converted to passive context for Chat requests. Switching is disabled during an active response. GUI slash commands and CLI commands provide /chat and /agent; branches retain the parent's mode.
- Simplifies the Preview pane's empty state by removing its redundant heading, format list and introductory text. Removes Preview from the Tools dropdown while retaining the tool-panel tab and previews opened from chat images and compatible file links. GUI, CLI and legacy desktop versions remain aligned.

## 1.51.0 - 2026-10-02

- Subagents announce and maintain their own structured task lists through todowrite. Lists are isolated from the parent conversation, retained in the existing transcript and preserved through context compaction. GUI chat bubbles, sidebar rows, detailed agent views and CLI bars track completed versus declared tasks, with the current task and cancelled-task count. Unknown plans remain indeterminate; unperformed or unfinished tasks are never automatically marked completed. The configured model-step budget remains unchanged and is no longer displayed as task progress. OpenCode task polling updates the corresponding child plan when its native tools are enabled.
- The native GUI's Parent conversation action is now an accent button in a fixed header above the subagent scroll area. It remains visible during scrolling and refreshes, returns to the parent chat and disappears when navigating away. Labels and agent statuses use the application's selected language, with new German/Spanish translations. The legacy desktop host also gains task-based progress and a sticky parent navigation header. GUI, CLI and legacy desktop versions remain aligned.

## 1.50.0 - 2026-10-02

- Renames the GUI Consumption tool to Dashboard and adds native Consumption and Speed tabs. Consumption retains its token totals, timeline, model distribution, call details and CSV export. Both tabs share Today / 3 / 7 / 30 days / 1 year, provider, model, activity and project filters, with automatic refresh every 15 seconds and independent detail pagination.
- The Speed tab uses existing recorded output tokens and request durations to show duration-weighted average throughput, slowest/fastest per-call throughput, measured-call count, a timeline, model comparisons and a filtered CSV export. Durations include waiting and reasoning; estimated output counts are labelled. Failed/interrupted calls and records without a model, positive output count or valid duration are excluded with an explicit count. No new database fields or additional model requests are needed. GUI, CLI and legacy desktop versions remain aligned.

## 1.49.2 - 2026-10-02

- Renames HTTP web research modes to Smart and Legacy throughout GUI/CLI settings, skill details, model instructions, tool schemas and new results. Smart remains the recommended default: models are instructed to search saved pages first, read only relevant passages, reuse cached results and stop when sufficient evidence is available. Legacy returns body and headers inline and is reserved for an explicit user choice or request. Historical partial/full preferences and tool calls remain accepted and map to Smart/Legacy; existing cached files stay readable. GUI, CLI and legacy desktop versions remain aligned.

## 1.49.1 - 2026-10-02

- Raises the standard context fallback from 128,000 to 256,000 tokens across the shared engine, new remote connections, model context controls, chat counters, scheduled-task defaults and the legacy desktop provider form. Former 128,000-token catalogue estimates use the same shared default. Provider-reported model capacities and explicit per-model overrides remain authoritative; local-model defaults and other model-specific estimates retain their existing values. Existing profiles in automatic mode pick up the new fallback without overwriting saved custom limits. GUI, CLI and legacy desktop versions remain aligned.

## 1.49.0 - 2026-10-02

- Adds Partial and Full HTTP response modes in GUI Skills → Web research and CLI settings. Partial is the default: web_http_request saves the original body, metadata and readable content in a conversation-scoped tool cache, returning a bounded preview and outline instead of the entire page and headers. New web_http_search and web_http_read tools provide literal search, numbered passages and pagination, including columns for long/minified lines. HTML text retains headings and links without executing JavaScript; raw content remains available. Full mode returns the downloaded body inline. Downloads are capped at 1 MiB, with explicit truncation. Existing network permissions, live skill checks, Plan and sandbox restrictions remain enforced.
- Conversation maintenance now offers Clean tool artifacts, enabled by default after seven days without use. Reading or searching a cached result renews retention. GUI and CLI clean the managed HTTP cache at startup and hourly, preserving active conversations and deleting only known cache files. Source folders, conversation documents and release binaries remain outside this cache. GUI and CLI versions remain aligned.

## 1.48.3 - 2026-10-02

- Adds a four-second grace period before showing model-retry notices in GUI chats, including OpenCode connections. The status chip and current response show Model thinking during this period; resumed requests or output cancel the delayed notice. Persistent retries keep their actual attempt count and remaining delay, and final errors remain visible. Retry timing, limits and compaction behavior are unchanged. GUI and CLI versions remain aligned.

## 1.48.2 - 2026-10-02

- Aligns the Providers title, Add provider menu and Back action in one fixed header row when space allows. Controls wrap together in narrower windows or with larger fonts. Back now clears the editor selection and reliably returns to the provider list while retaining unsaved drafts; leaving the name field no longer rebuilds or reopens the provider form. GUI and CLI versions remain aligned.

## 1.48.1 - 2026-10-02

- Keeps historical local file links, previews and approved file reads working after the portable-folder migration. GUI and CLI resolve legacy locations before protected-file checks and access approvals, preserving history text and permission boundaries. GUI and CLI versions remain aligned.

## 1.48.0 - 2026-10-02

- Conversations without an attached folder now receive a private file workspace under `workspace/conversations/chat-<id>` beside the executable or selected database. GUI and CLI source tools and terminals use it automatically, including conversations with file-only resources. Reopening a chat restores its files; attaching a real project folder uses that folder instead. Plan mode, enabled skills and approvals still apply.
- Groups application-managed folders under one portable `workspace/` directory: skills, models, browser profiles, drawings, generated images, scripts, Python, sandboxes, logs and exports. Startup moves legacy folders and updates saved paths without relocating external projects or the SQLite database. Existing destination folders retain their contents; conflicting legacy folders are preserved under `workspace/legacy-imports/`. Interrupted moves resume on a later startup. Conversation files are retained when chats are deleted. GUI and CLI versions remain aligned.

## 1.47.3 - 2026-10-02

- Restyles the GUI generation-speed popover to match the context panel: an accent average-speed summary, a live/estimated or last-response badge, and rounded cards for the response and conversation statistics. Minimum, average and maximum values remain separate, with missing measurements shown as dashes. The layout adapts to narrow windows and text zoom, keeps scrolling on shorter screens and refreshes while open. Existing throughput calculations and session-only extrema are preserved. GUI and CLI versions remain aligned.

## 1.47.2 - 2026-10-02

- Keeps the GUI thinking status chip at “Le modèle réfléchit” while the model reasons, instead of repeating excerpts or an idle counter in that chip. The separate reasoning panel still displays the full streamed reasoning, and the response and tool status indicators retain their existing behavior. GUI and CLI versions remain aligned.

## 1.47.1 - 2026-10-02

- The GUI Model and thinking panel now includes a settings icon immediately before model refresh. It opens Settings directly on Providers, reuses an already-open settings window and preserves unsaved edits. The destination is also retained when settings are still loading.
- The Providers title, Add provider menu and editor Back button stay visible while provider cards or long forms scroll. Opening a provider, adding one or returning to the list resets the content to the top; ordinary field edits retain their scroll position. GUI and CLI versions remain aligned.

## 1.47.0 - 2026-10-02

- Adds a native folder picker beside the local-model directory in GUI provider settings. The selected directory stays editable and is saved with the provider; cancelling keeps the previous path.
- Replaces the Hugging Face modal with an independent, resizable model browser. Search and model-type filters sit above a model list and a detail pane with repository metadata, downloadable files, machine compatibility estimates and the model card. The panes stack in smaller windows, and download/import actions stay at the bottom right. Unsupported components remain visible with their reasons; cancelling or closing stops transfers and removes partial files. GUI and CLI versions remain aligned.

## 1.46.0 - 2026-10-02

- GUI About settings now offer three automatic-update levels: disabled (no automatic checks), notify only, or install automatically. Enabled modes check GitHub at startup and every two hours while the application is open. Existing preferences are preserved; notification remains the default and manual checks stay available in every mode.
- A persistent Update button appears above Scheduled tasks when a newer compatible GUI release is available. It uses the existing verified download and restart flow. Automatic installation waits for agents, conversation drafts and editing windows to finish, rechecks before restarting, and preserves portable data; installation remains limited to standalone Windows releases.

## 1.45.2 - 2026-10-02

- Fixes SQLite rollback of the local-model, cached-token and per-model context migrations. Returning to an older schema and upgrading again no longer fails on an unsupported EF table-rebuild operation; normal forward upgrades keep their existing behavior.

## 1.45.1 - 2026-10-02

- Compacts the GUI Model and thinking panel with a narrower responsive width, smaller spacing and inputs, an inline thinking selector and a context editor on two short rows. Removes repeated explanatory labels from this popup; thinking guidance moves to a tooltip, and context detection details remain available from an information button. Automatic/custom limits, model autocomplete and the accent Done action remain accessible.

## 1.45.0 - 2026-10-01

- Context capacity is now saved for each exact model ID within its provider connection. Switching models restores their own automatic or custom limit, without carrying a limit from the previous model. GUI provider settings, onboarding and the Model and thinking panel share a context editor; the CLI provider menu adds Context per model. Context counters, request sizing, agent models and automatic compaction use the selected model's capacity.
- Loading or refreshing models retains explicit context-window metadata from compatible catalogues and OpenCode, and obtains Gemini's input-token limits from its native model catalogue. Automatic mode uses the reported maximum, within the engine's supported ceiling; output-token caps are never used as context capacity. Missing metadata is clearly labelled as an estimated fallback and can be overridden for that model. Model refresh preserves custom settings.
- Local GGUF chat imports read the architecture's declared context window from bounded metadata, without loading model weights. Refreshing local models also detects this for older imports. The local engine ceiling and available-memory requirements still apply; a custom context can reduce memory use.
- Migrates historical non-default provider limits to a custom limit for the model previously selected. Historical standard defaults become automatic; model settings and reported capacities persist across application restarts.

## 1.44.1 - 2026-10-01

- GUI replies display reused input tokens in the same footer as duration and response time, with the same typography and hover visibility. The footer reserves its space before hovering and wraps in narrow bubbles without shifting other messages. Provider-reported zero remains visible; an absent cache counter adds no label. The combined tooltip keeps the response date and explains reported cache reuse.
- Removes the model picker's explanatory text and result/suggestion counts. Empty-search and unconfigured-model feedback remains available when needed.

## 1.44.0 - 2026-10-01

- Redesigns the GUI Model and thinking panel with a responsive width, separate model and thinking sections, connection details, explanatory thinking text and a Done action at the bottom right. Long names wrap in suggestions, body scrolling stays vertical, and model refresh shows a busy indicator.
- Adds model autocomplete from the enabled models of every configured connection. Typing filters model and provider names without changing the active model; matching model prefixes and the current provider are prioritized. Arrow keys navigate suggestions; Enter or clicking a suggestion confirms a choice, while a browse button shows the catalogue. Unknown or ambiguous names never create a model or silently switch provider. Suggestions are limited to 50 visible items with a refinement hint for large catalogues.
- Closing the panel discards unconfirmed search text. Confirmed choices retain the existing immediate-save behavior, and local model refresh no longer requires a remote API key.

## 1.43.0 - 2026-10-01

- Replaces “Préchargement du contexte” with “Chargement du modèle / Loading model” in GUI and CLI, showing Preparation, Sending and Waiting for first tokens. The wait includes estimated context size and elapsed time; GUI request details clarify that these are observable request stages rather than evidence of a model restart or server progress. Local engine preparation is included in the first stage.
- Reads provider-reported input cache hits from compatible usage metadata, including DeepSeek, OpenRouter and OpenCode cache reads. Completed replies show reused tokens in a small footer and the GUI context details; CLI history shows the same counter. Missing counters remain unknown and are never estimated or confused with a reported zero. Cached tokens still occupy context and are not subtracted from its size.
- Persists nullable cache counters with each reply and consumption record so declared reuse remains available after reopening the conversation. For OpenCode calls with several assistant turns, consumption totals aggregate cache reads only when every turn reports them. Existing conversation rows remain unknown until a new request reports usage.

## 1.42.1 - 2026-10-01

- GUI tool cards appear as soon as an execution starts, including while waiting for the shared tool queue or user permission. Each card shows an activity indicator, the supplied arguments and elapsed time; the same card becomes the final success, failure or stopped result with expandable details and any returned image. Empty assistant tool turns show their activity immediately instead of leaving a blank bubble until completion.
- Allows continuous text selection across adjacent Markdown headings, paragraphs, lists and quotes, including list numbers and bullet markers. Streamed replies reuse their text surface and completed inlines; file context menus attach once and recognize newly received links. Code blocks and tables keep their dedicated rendering.

## 1.42.0 - 2026-10-01

- Adds OpenRouter, Groq, Google Gemini, Mistral, Z.ai and NVIDIA NIM to the GUI provider menu and CLI connection wizard, backed by one shared preset catalogue. Each connection keeps its own protected key and model selection; GUI settings show service information, API-key and documentation links. Account quotas and billing remain with the provider.
- Uses each service's official compatible API endpoint. Model discovery keeps the authenticated catalogue, filters explicitly incompatible chat capabilities and normalizes Gemini model IDs. Documented Z.ai examples are labelled as unverified and can be entered manually when model discovery is unavailable; they never count as a successful connection check.
- Handles OpenRouter reasoning parameters and opaque reasoning blocks, Gemini tool-call thought signatures, Mistral's streamed thinking/text chunks, Groq usage metadata and GLM's enabled/disabled thinking switch. Required metadata is preserved across tool turns. Compatibility notices explain unsupported effort levels and forced GLM thinking.
- Makes Gemini and Z.ai available to the image skill through their image APIs, with Gemini base64 responses and Z.ai's documented image identifiers and dimension checks. Other new presets are excluded from this skill's provider list because they require different image APIs. Existing generic compatible and Local image providers remain available.

## 1.41.0 - 2026-10-01

- Adds the opt-in **Image generation · Beta** skill with the `generate_image` tool, a dedicated provider/model selector, image dimensions and local sampling steps. Generated images appear in the conversation and are saved under `images/chat-…/`. Chat permissions, Plan/sandbox restrictions and cancellation apply to both GUI and CLI; remote providers must implement the OpenAI-compatible `images/generations` API.
- Adds a **Local · Beta** provider in GUI settings. Import or reference standalone GGUF chat models and complete Stable Diffusion 1.x, 2.x or SDXL Safetensors checkpoints. The default model directory is `model/` next to the executable. Image-only providers remain available to the skill without appearing as chat providers.
- Adds a Hugging Face search dialog for chat and image models, file/quantization selection, licence/model-card links, gated access through an optional protected read token, cancellable streaming downloads and SHA-256 checks when the Hub provides a digest. Incomplete checkpoints, projectors, adapters, split files and separate Diffusers components explain why they cannot be loaded by this beta.
- Shows estimated support and memory requirements based on detected OS/architecture, CPU instruction support, installed/free RAM, graphics adapters and VRAM when available. CPU/partial GPU operation, unknown hardware and insufficient memory are distinguished; actual architectures, drivers and throughput are confirmed only when loading/inference runs.
- Downloads official llama.cpp and stable-diffusion.cpp engines on explicit Prepare / load, verifies their published SHA-256, and supports CPU/Vulkan selection or already installed engine paths. Chat servers bind only to loopback with a generated credential, are reused between requests and can be unloaded while idle. Image models load for each generation and release their process afterwards. Heavy transfers and image parsing use asynchronous/background work.

## 1.40.0 - 2026-10-01

- Tools adds Preview in the right workspace pane, with real Raw and Preview tabs. Clicking a local PNG/JPEG/WebP/GIF/BMP/ICO, TXT or Markdown file in the chat opens it directly; attached images and compatible text attachments use the same pane. Existing right-click OS, browser and containing-folder actions remain available, with an additional Preview action for compatible files.
- Preview renders Markdown headings, lists, tables and code, plain text and images with fit/zoom controls. Local Markdown image references are rendered when readable inside the document's directory. Raw shows the original decoded text or paginated hexadecimal bytes and image metadata. The pane also offers a file picker, refresh, raw copy and file actions.
- File reads, text decoding, image conversion and Markdown parsing run in the background. Text and binary pages, image size limits, bounded Markdown rendering, cancellation on navigation and request revisions keep large or stale previews from blocking the chat. No scripts are executed and embedded remote images are not fetched.

## 1.39.0 - 2026-10-01

- GUI and CLI show “Préchargement du contexte / Preloading context” before the first actual output tokens, with an estimated context size and elapsed wait. GUI tooltips describe request preparation, sending/connection and waiting for output, message count and encoded request size when known. These are observed client stages, not a provider prefill percentage or cache status.
- The indicator switches to reasoning or response activity on the first meaningful text, reasoning or tool-call fragment. Empty role/heartbeat chunks keep the preloading state. New requests and automatic retries restart their wait counter; switching conversations preserves the associated request state. Generation token/speed accounting is unchanged.

## 1.38.0 - 2026-10-01

- Appearance adds Compact / Normal / Spacious chat density, controlling text size, line height, message spacing and bubble padding, including displayed history. Lists no longer add duplicate blank lines. Shift+Enter uses the native multiline editor once.
- Automatic conversation naming can start after the first user message or after the first completed response, with the timing shared by GUI and CLI.
- General settings add a switchable Needs attention section above pinned conversations and a Windows rendering GPU preference (system choice, high performance or power saving), applied to the current executable after restart.
- Tool results show their measured execution duration and retain it in history. The current tool chip shows a concise description from its arguments and an elapsed-time counter.
- Optional automatic archival and deletion of archived conversations use configurable total inactivity delays (30 and 45 days by default). They are disabled initially and run at startup and hourly while the GUI is open. Displayed, pinned, favorite, running or pending conversations are preserved; deletion removes conversation-owned records.

## 1.37.0 - 2026-09-30

- Translator and proofreader editors synchronize after changes rather than polling continuously. Formatted paste is processed in bounded batches, with stack-safe traversal and explicit size/complexity errors that preserve existing text. Redundant editor replacements and layout changes are avoided to keep windows responsive and prevent blinking.
- AI Generated detector can use a configured provider and model instead of SlopTotal for pasted text and locally extracted TXT/MD/PDF/DOCX documents. The selection is saved, requests use the configured connection and retry settings, cancellation is supported and token consumption is recorded. Model assessments are labeled as uncalibrated; scores, paragraph references and exact source quotations are validated. SlopTotal retains web-page and site fingerprint analysis.

## 1.36.0 - 2026-09-30

- Automated publication is temporarily limited to Windows x64 and Linux x64 for both GUI and CLI. macOS platform code and already published historical downloads remain available.

- Renamed all .NET projects, namespaces, assemblies, embedded resources, application identifiers and build references to MonolithHarness. GUI and CLI keep their separate portable folders and the same MonolithHarness launcher name.
- New release packaging uses MonolithHarness archive names; Windows packages also retain compatibility aliases for automatic updates from previously installed versions. The updater recognizes both naming schemes and prefers the new name.
- Existing databases, Windows/Linux encrypted keys, macOS Keychain entries and index.ohm files remain readable. New macOS keys and file-index writes use the new product identifiers; custom application names remain unchanged.

## 1.35.0 - 2026-09-30

- New Settings → Compaction tab: automatic compaction toggle, trigger threshold and context target, Gentle/Balanced/Strong/Custom strength, custom summary ratio and token budget, custom instructions and three strategies (recent exchanges, entire history, tool exchanges first). Recent exchanges can be prioritized, and invalid percentages keep settings open with an explanation. Default: trigger at 90%, target 60%, Balanced strength.
- GUI, CLI and compatible subagents share the same compaction policy. CLI `/settings` also exposes these preferences; the context popover displays the configured threshold and target. Settings apply to subsequent requests and manual compaction remains available when automation is off.
- Compaction summarizes all selected segments without silent truncation, merges partial summaries to fit its budget and validates the final reduction against the target before archiving anything. Completed tool calls remain paired with their results; previous summaries no longer block compaction of long autonomous runs. Original messages remain in the database on failure or an unreachable target.
- OpenCode GUI compaction also captures native tool outcomes before producing bounded summaries and starts a fresh continuation session only after successful compaction.

## 1.34.0 - 2026-09-30

- The Windows CLI executable now has its own terminal-window icon around the Monolith M, making it easy to distinguish from the GUI executable.
- Thinking is shown as an animated bar in the empty composer. Typing immediately restores the editor, so a follow-up message can still be prepared and queued while the model works.
- Plain Up/Down recalls previous messages and commands for the current conversation, restoring the unfinished draft when returning past the latest entry. Saved user messages remain available after restarting; multiline editing and slash completion keep their navigation shortcuts.
- Subagents have a dedicated live panel with one animated activity bar per worker and its current action. Completed, failed and stopped workers are distinguished; Alt+PageUp/PageDown pages through larger teams. Active bars are indeterminate rather than treating the step budget as a completion percentage.

## 1.33.1 - 2026-09-30

- Project creation now uses the same editor as Manage project: name, icon, icon color with preview, multiple default folders and optional permission-file import. The first conversation inherits the selected folders immediately.
- Repeated folder selections are deduplicated. Invalid names or folders keep the editor open with an inline error; canceling creation saves no project or conversation.

## 1.33.0 - 2026-09-30

- Added Tools > AI Generated detector in a separate native window, connected to the real SlopTotal detection service. Scan pasted prose, web-page text and PDF/DOCX/TXT/MD documents, with a calibrated global score, live engine results, filters, explanations, per-paragraph score charts and a copyable JSON report.
- Website scans also identify AI app-builder fingerprints, with supporting evidence shown separately from text detection. Short-text limitations, server retention and unavailable engines are explicitly surfaced; detector scores are indicators, not proof of authorship.
- Configure and remember a local or remote SlopTotal endpoint. An optional user-started Docker installer builds a pinned upstream revision with CPU models and document support, binds it only to localhost, and preserves models/reports in dedicated volumes. Docker must be installed and running; setup is never triggered automatically.

## 1.32.0 - 2026-09-30

- Added Tools > Token consumption in a separate window, with token timelines, model breakdowns, input/output totals and paginated call details. Filters cover Today, 3 days, 7 days, 30 days and 1 year, plus provider, model, project and activity. The dashboard refreshes automatically and filtered details can be copied as CSV.
- GUI and CLI persist consumption for chats, subagents, naming, vision, compaction, translation, proofreading and benchmarks. Provider-reported counters are distinguished from estimates, including partial responses on cancellation or failure. Input tokens count each call's prompt, including repeated history.
- A database migration imports dated historical message counters with the available metadata and avoids counting copied fork messages twice; undated replies are explicitly excluded from period totals. Usage records contain no prompts, responses or credentials, survive individual chat deletion, and are erased by the existing data reset.

## 1.31.1 - 2026-09-30

- The chat's model, speed and context section now adapts to its actual available width and font zoom. A combined model button displays the selected model without truncation and opens model, thinking level and refresh controls together; `/model` still opens model selection.
- Context usage has a bounded, smaller gauge. Narrow layouts give model selection the full row and retain detailed token counts in the context popover, with additional stacking for very small widths.
- Removed the duplicate model-thinking text from the model information section. Chat progress and the no-response timer remain in the conversation's status area.

## 1.31.0 - 2026-09-30

- New GUI profiles open an illustrated, three-step welcome guide: preview and choose a theme, configure an AI provider and model, then select skills. Essentials and Development presets help get started, with independent Git reading and writing controls.
- Provider setup supports compatible APIs and OpenCode, loading model catalogs on request or entering a model ID manually. Credentials use the existing system vault; theme, provider and skill choices are saved together at completion.
- Setup can be deferred and reopened from Settings > About. Existing profiles retain their settings and do not receive the first-launch prompt. Illustrations follow the selected theme, and the guide adapts to smaller windows.

## 1.30.0 - 2026-09-30

- Local file and directory paths in chat now have a context menu with Open, Open in browser and Open containing folder. Relative paths use the conversation's source folders; missing or ambiguous paths report an explicit error.
- Path links support directories, quoted paths and inline code with spaces, file URLs and line references. The menu preserves selectable text and wrapping in streamed replies, code blocks, tool details and subagent transcripts.
- The embedded browser can display an approved local directory and navigate its files and subdirectories, retaining its existing access checks and protected-file exclusions.

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

- Added MonolithHarness CLI, a full-screen terminal workspace with searchable commands and conversations, concurrent streaming chats, model/provider selection, Plan/Execution modes, approvals, agent questions, task lists, subagent inspection, memory, MCP, Git diffs, terminals, attachments, queue/steering and Markdown exports. It reuses the shared agent engine and portable SQLite storage.
- Added non-interactive runs with plain text or JSON events, explicit execution/approval options, and standalone CLI publishing for Windows and macOS.

## 1.0.1 — 2026-09-23

- A standalone executable started in a new folder now creates a fresh portable database instead of silently copying conversations and settings from a previous AppData installation. Legacy browser and OpenCode workspace folders are no longer imported automatically. Existing `database.sqlite` files beside the executable remain untouched and continue to migrate normally.
