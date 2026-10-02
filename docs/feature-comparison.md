# Feature comparison: scope and sources

The table at the bottom of the [README](../README.md#feature-comparison) compares documented workflows, inspected on **2026-10-02**. Monolith Harness is version **1.46.0**. Peer references are pinned to the source revisions below; their default branches may include features newer than a stable download. This is a documentation review, without running benchmarks or testing the other applications.

**✅** means a documented built-in workflow, with normal configuration. **⚠️** means partial coverage, extra integration, experimental availability, or a different workflow. **❌** means the exact bundled workflow was not documented in the inspected standard interfaces. A cross does not establish that an extension, script, or custom SDK application could never implement it.

## Monolith Harness

- [Portable GUI/CLI, usage charts and detector](../README.md), [portable data and application features](guide.md).
- [Conversation/shared memory, permissions, editor and database viewer](memory.md). Memory uses SQLite lexical search; semantic source retrieval is a separate skill.
- [Embedded browser, local multilingual RAG and subagents](browser-rag-agents.md). The local embedding weights are included in the executable; optional API embeddings are separate. Browser platform requirements still apply.
- [Formatted translator, proofreader and model benchmark](model-tools.md); [editable asset canvas, animations and exports](asset-generator.md).
- [Scheduling](uno-tasks-models.md#project-tasks) requires an open application. [Local chat and image models](local-models-and-images.md) are Beta and need separately downloaded models/runtimes.
- [GitHub updates](cli.md#github-updates): startup and two-hour checks are a GUI feature. Notify is the default; installation requires a published standalone Windows executable. Linux installation remains manual.

## Pi

Source: [earendil-works/pi at 7fbbd5f](https://github.com/earendil-works/pi/tree/7fbbd5f4a1d982bb02d63472dde0774fa639f99b). The older badlogic/pi-mono repository URL redirects here.

- [Coding-agent documentation](https://github.com/earendil-works/pi/blob/7fbbd5f4a1d982bb02d63472dde0774fa639f99b/packages/coding-agent/docs/index.md) and [interfaces, sessions and extensions](https://github.com/earendil-works/pi/blob/7fbbd5f4a1d982bb02d63472dde0774fa639f99b/packages/coding-agent/docs/how-pi-works.md) describe a terminal agent with print/JSON/RPC and a TypeScript SDK. Custom clients are possible; a bundled project desktop app is outside this coding-agent scope.
- [MCP](https://github.com/earendil-works/pi/blob/7fbbd5f4a1d982bb02d63472dde0774fa639f99b/packages/coding-agent/docs/mcp.md) is built in at this revision. [Subagents](https://github.com/earendil-works/pi/blob/7fbbd5f4a1d982bb02d63472dde0774fa639f99b/packages/coding-agent/examples/extensions/subagent/README.md) are an installable example extension.
- [Settings](https://github.com/earendil-works/pi/blob/7fbbd5f4a1d982bb02d63472dde0774fa639f99b/packages/coding-agent/docs/settings.md) and [llama.cpp models](https://github.com/earendil-works/pi/blob/7fbbd5f4a1d982bb02d63472dde0774fa639f99b/packages/coding-agent/docs/llama-cpp.md) support customization and an externally installed router. Instructions, session history and extensions are not the same workflow as Monolith's curated memory viewer or bundled semantic index.
- Recurring jobs can use external scheduling of Pi's print/headless commands. The inspected coding-agent documentation does not describe a built-in schedule editor.

## OpenCode

Source: [anomalyco/opencode at a79ecfe](https://github.com/anomalyco/opencode/tree/a79ecfe109294909a239c98fb89f02979d5aa10b).

- [README and desktop downloads](https://github.com/anomalyco/opencode/blob/a79ecfe109294909a239c98fb89f02979d5aa10b/README.md), [agents](https://github.com/anomalyco/opencode/blob/a79ecfe109294909a239c98fb89f02979d5aa10b/packages/web/src/content/docs/agents.mdx), [skills](https://github.com/anomalyco/opencode/blob/a79ecfe109294909a239c98fb89f02979d5aa10b/packages/web/src/content/docs/skills.mdx) and [MCP](https://github.com/anomalyco/opencode/blob/a79ecfe109294909a239c98fb89f02979d5aa10b/packages/web/src/content/docs/mcp-servers.mdx) document its coding-focused CLI and desktop workflows.
- [CLI reference](https://github.com/anomalyco/opencode/blob/a79ecfe109294909a239c98fb89f02979d5aa10b/packages/web/src/content/docs/cli.mdx) documents headless runs, usage/cost statistics, exports, database paths and upgrades. These cover part of Monolith's consumption workflow; the complete GUI timeline plus filtered CSV workflow was not established by this review.
- [Configuration](https://github.com/anomalyco/opencode/blob/a79ecfe109294909a239c98fb89f02979d5aa10b/packages/web/src/content/docs/config.mdx) and [plugins](https://github.com/anomalyco/opencode/blob/a79ecfe109294909a239c98fb89f02979d5aa10b/packages/web/src/content/docs/plugins.mdx) explain instructions, update controls and integrations. Plugin/retrieval/browser possibilities receive a warning rather than a claim that they are impossible.

## Hermes Agent

Source: [NousResearch/hermes-agent at be5e9f7](https://github.com/NousResearch/hermes-agent/tree/be5e9f72c6681af9dfb75bf480f08844f1499949).

- [Agent README](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/README.md) documents messaging gateways, skills learned from experience, delegation, cron, usage and insights. [Desktop](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/apps/desktop/README.md) includes projects, previews and a shared agent backend; its data lives in a separate Hermes home.
- [Memory](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/website/docs/user-guide/features/memory.md) uses curated memory/user files and session search. External memory providers can add other retrieval capabilities.
- [Cron](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/website/docs/user-guide/features/cron.md), [browser automation](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/website/docs/user-guide/features/browser.md) and [image generation](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/website/docs/user-guide/features/image-generation.md) are real capabilities. The image tools and browser backends differ from Monolith's local checkpoint manager and per-conversation embedded tabs.
- [Local models](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/website/docs/user-guide/local-models.md) include managed llama.cpp and Hugging Face downloads; the desktop interface is canary/launch-flag gated at this revision. [Updates](https://github.com/NousResearch/hermes-agent/blob/be5e9f72c6681af9dfb75bf480f08844f1499949/website/docs/getting-started/updating.md) depend on the package owner or source-install workflow.

## LangChain Deep Agents

Source: [langchain-ai/deepagents at 372bc22](https://github.com/langchain-ai/deepagents/tree/372bc221ee8e66885f25e22f42bd8168a21c3ac7). This column means **LangChain Deep Agents**, including the SDK and **Deep Agents Code** terminal application, rather than Abacus.AI DeepAgent.

- [SDK overview](https://github.com/langchain-ai/deepagents/blob/372bc221ee8e66885f25e22f42bd8168a21c3ac7/README.md) includes subagents, context management, memory backends, skills and custom/MCP tools. [Deep Agents Code](https://github.com/langchain-ai/deepagents/blob/372bc221ee8e66885f25e22f42bd8168a21c3ac7/libs/code/README.md) adds a ready-to-use terminal interface, memory, resume, search and headless mode.
- [MCP integration](https://github.com/langchain-ai/deepagents/blob/372bc221ee8e66885f25e22f42bd8168a21c3ac7/openwiki/integrations/mcp.md) and [cost/session operations](https://github.com/langchain-ai/deepagents/blob/372bc221ee8e66885f25e22f42bd8168a21c3ac7/openwiki/operations/cost-and-sessions.md) explain configurable tools and estimates. LangSmith adds optional tracing/evaluation; that is a different workflow from Monolith's bundled benchmark and consumption windows.
- [Talon](https://github.com/langchain-ai/deepagents/blob/372bc221ee8e66885f25e22f42bd8168a21c3ac7/libs/talon/README.md) adds a cron host and messaging adapters, explicitly marked experimental alpha. Scheduling and gateways therefore receive a warning, while their availability is acknowledged.
