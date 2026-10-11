# Feature comparison: scope and sources

The table at the bottom of the [README](../README.md#feature-comparison) compares documented workflows, inspected on **2026-10-11**. The reviewed Monolith Harness version was **1.89.0**. The peer scope covers Pi's coding agent, OpenCode's CLI/desktop app, Hermes Agent/Desktop, and LangChain Deep Agents with Deep Agents Code. Peer references are pinned to the source revisions below; their default branches may include features newer than a stable download. This is a documentation review, without running benchmarks or testing the other applications.

**✅** means a documented built-in workflow, with normal configuration. **⚠️** means partial coverage, extra integration, experimental availability, or a different workflow. **❌** means the exact bundled workflow was not documented in the inspected standard interfaces. A cross does not establish that an extension, script, or custom SDK application could never implement it.

## Reviewed snapshot

| Project | Reviewed source |
| --- | --- |
| Monolith Harness | Local GUI/CLI **1.89.0** executables and the current desktop skill catalog; Flutter Android **1.89.0+125** source preview |
| Pi | Coding-agent package **1.1.0**, revision `cdaf6c227d4718a72b59ccd02152c9614df44cfc` |
| OpenCode | Desktop package **2.0.26**, revision `d21b0790a6c146815f28fea604e41e922f009590` on `dev` |
| Hermes Agent | Revision `7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3` on `main`, including Desktop and the documented Termux distribution |
| Deep Agents | SDK **0.7.23**, Code **0.1.83**, Talon **0.0.9**, revision `4bb4c7fcbf5f2ef65c39ea0134c42555926bd40b` |

Package versions describe the inspected source, not a claim that every workflow is in a stable published release. The Monolith download links still point to the published **1.88.1** releases; local **1.89.0** screenshots and the Flutter source preview do not imply a new GitHub release.

## Monolith Harness

- [Portable GUI/CLI, usage charts and detector](../README.md), [portable data and application features](guide.md).
- [Conversation/shared memory, permissions, editor and database viewer](memory.md). Memory uses SQLite lexical search; semantic source retrieval is a separate skill.
- [Embedded browser, local multilingual RAG and subagents](browser-rag-agents.md). The local embedding weights are included in the executable; optional API embeddings are separate. Browser platform requirements still apply.
- [Formatted translator, proofreader and model benchmark](model-tools.md); [editable asset canvas, animations and exports](asset-generator.md).
- [Scheduling](uno-tasks-models.md#project-tasks) requires an open application. [Local chat and image models](local-models-and-images.md) are Beta and need separately downloaded models/runtimes.
- [GitHub updates](cli.md#github-updates): startup and two-hour checks are a GUI feature. Notify is the default; installation requires a published standalone Windows executable. Linux installation remains manual.

## Pi

Source: [earendil-works/pi at cdaf6c2](https://github.com/earendil-works/pi/tree/cdaf6c227d4718a72b59ccd02152c9614df44cfc). The older badlogic/pi-mono repository URL redirects here.

- [Coding-agent documentation](https://github.com/earendil-works/pi/blob/cdaf6c227d4718a72b59ccd02152c9614df44cfc/packages/coding-agent/docs/index.md) and [interfaces, sessions and extensions](https://github.com/earendil-works/pi/blob/cdaf6c227d4718a72b59ccd02152c9614df44cfc/packages/coding-agent/docs/how-pi-works.md) describe a terminal agent with print/JSON/RPC and a TypeScript SDK. Custom clients are possible; a bundled project desktop app is outside this coding-agent scope.
- [MCP](https://github.com/earendil-works/pi/blob/cdaf6c227d4718a72b59ccd02152c9614df44cfc/packages/coding-agent/docs/mcp.md) is built in at this revision. [Subagents](https://github.com/earendil-works/pi/blob/cdaf6c227d4718a72b59ccd02152c9614df44cfc/packages/coding-agent/examples/extensions/subagent/README.md) are an installable example extension.
- [Settings](https://github.com/earendil-works/pi/blob/cdaf6c227d4718a72b59ccd02152c9614df44cfc/packages/coding-agent/docs/settings.md) and [local models and compatible endpoints](https://github.com/earendil-works/pi/blob/cdaf6c227d4718a72b59ccd02152c9614df44cfc/packages/coding-agent/docs/models.md) support customization and an externally installed router. Instructions, session history and extensions are not the same workflow as Monolith's curated memory viewer or bundled semantic index.
- Recurring jobs can use external scheduling of Pi's print/headless commands. The inspected coding-agent documentation does not describe a built-in schedule editor.

## OpenCode

Source: [anomalyco/opencode at d21b079](https://github.com/anomalyco/opencode/tree/d21b0790a6c146815f28fea604e41e922f009590).

- [README and desktop downloads](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/README.md), [agents](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/agents.mdx), [skills](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/skills.mdx) and [MCP](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/mcp-servers.mdx) document its coding-focused CLI and desktop workflows.
- [CLI reference](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/cli.mdx) documents headless runs, usage/cost statistics, exports, database paths and upgrades. These cover part of Monolith's consumption workflow; the complete GUI timeline plus filtered CSV workflow was not established by this review.
- [Configuration](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/config.mdx) and [plugins](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/plugins.mdx) explain instructions, auto/notify update controls and integrations. Plugin/retrieval/browser possibilities receive a warning rather than a claim that they are impossible.

## Hermes Agent

Source: [NousResearch/hermes-agent at 7adb97f](https://github.com/NousResearch/hermes-agent/tree/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3).

- [Agent README](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/README.md) documents messaging gateways, skills learned from experience, delegation, cron, usage and insights. [Desktop](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/apps/desktop/README.md) includes projects, previews and a shared agent backend; its data lives in a separate Hermes home.
- [Memory](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/website/docs/user-guide/features/memory.md) uses curated memory/user files and session search. External memory providers can add other retrieval capabilities.
- [Cron](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/website/docs/user-guide/features/cron.md), [browser automation](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/website/docs/user-guide/features/browser.md) and [image generation](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/website/docs/user-guide/features/image-generation.md) are real capabilities. The image tools and browser backends differ from Monolith's local checkpoint manager and per-conversation embedded tabs.
- [Local models](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/website/docs/user-guide/local-models.md) include managed llama.cpp and Hugging Face downloads; the desktop interface is canary/launch-flag gated at this revision. [Updates](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/website/docs/getting-started/updating.md) depend on the package owner or source-install workflow.

## LangChain Deep Agents

Source: [langchain-ai/deepagents at 4bb4c7f](https://github.com/langchain-ai/deepagents/tree/4bb4c7fcbf5f2ef65c39ea0134c42555926bd40b). This column means **LangChain Deep Agents**, including the SDK and **Deep Agents Code** terminal application, rather than Abacus.AI DeepAgent.

- [SDK overview](https://github.com/langchain-ai/deepagents/blob/4bb4c7fcbf5f2ef65c39ea0134c42555926bd40b/README.md) includes subagents, context management, memory backends, skills and custom/MCP tools. [Deep Agents Code](https://github.com/langchain-ai/deepagents/blob/4bb4c7fcbf5f2ef65c39ea0134c42555926bd40b/libs/code/README.md) adds a ready-to-use terminal interface, memory, resume, search and headless mode.
- [MCP integration](https://github.com/langchain-ai/deepagents/blob/4bb4c7fcbf5f2ef65c39ea0134c42555926bd40b/openwiki/integrations/mcp.md) and [cost/session operations](https://github.com/langchain-ai/deepagents/blob/4bb4c7fcbf5f2ef65c39ea0134c42555926bd40b/openwiki/operations/cost-and-sessions.md) explain configurable tools and estimates. LangSmith adds optional tracing/evaluation; that is a different workflow from Monolith's bundled benchmark and consumption windows.
- [Talon](https://github.com/langchain-ai/deepagents/blob/4bb4c7fcbf5f2ef65c39ea0134c42555926bd40b/libs/talon/README.md) adds a cron host and messaging adapters, explicitly marked experimental alpha. Scheduling and gateways therefore receive a warning, while their availability is acknowledged.

## Mobile and new workflow qualifications

- **Android:** Monolith has a Flutter source preview with a touch UI. The former Uno Android project has been retired from the current sources; its previously published APK remains available in historical releases. Their databases and keys are separate, with no automatic synchronization. Flutter has no desktop terminal/control, MCP, local chat/image models or desktop proposal review. See the [Flutter guide](flutter-android.md) and [capture notes](images/readme/README.md).
- **Hermes on Android:** the inspected [README](https://github.com/NousResearch/hermes-agent/blob/7adb97f22abb9c060ad7b0798e9436e5bfc4b8e3/README.md) documents a signed aarch64 Termux package with its TUI. This is real Android access, but a different workflow from a touch-first standalone APK.
- **HTML previews:** Monolith's [rich replies](reponses-riches.md) include native cards and isolated interactive previews in GUI and Android, with local Tailwind/Chart.js. Hermes documents desktop side-pane previews. No matching built-in native-card/fullscreen-app workflow was established for Pi, OpenCode or Deep Agents in the sources listed here; extensions, custom clients and browser tools can implement related behavior.
- **Image generation:** Monolith exposes a dedicated [image skill](../src/MonolithHarness.Core.Desktop/ImageGenerationTools.cs) using a configured local or image-API model. Hermes has a documented image-generation tool. Custom tools or MCP can add generators to the other products; this does not establish a bundled checkpoint/engine manager.
- **Sandboxing:** Monolith offers optional Docker/Podman source copies with proposal review. Hermes documents local/container/remote terminal backends. Deep Agents Code documents remote sandboxes. The inspected Pi and OpenCode sources do not establish the same bundled container/remote execution workflow; external setup is possible. Tool approval alone is not container isolation.
- **Update controls:** OpenCode documents `autoupdate: true`, `false` and `"notify"`; its cell is now marked built in. Hermes updates depend on the owner of the installed package. Monolith's automatic replacement remains a Windows GUI feature; the CLI and other platforms have their own update instructions.

## How to read the specialized rows

A missing workflow means it was not established in the inspected standard interfaces. It does not mean that a general-purpose agent cannot translate text, write an HTML app, draw an SVG, call an image provider or calculate usage statistics. The specialized rows compare the named bundled editor/viewer/export workflow, rather than those general capabilities.

The sources were read for this update; the four peer applications were not launched or benchmarked. Monolith screenshot diagnostics are scoped UI checks, and their results and failures are documented separately in the [capture notes](images/readme/README.md).