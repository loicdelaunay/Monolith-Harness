# Questions and action suggestions (desktop)

Question cards use the active application theme. Choices appear as responsive tiles. Multiple questions share an accordion with answer summaries, previous/next navigation and final validation. Pure free-text questions show one text field. A question selects exactly one answer unless `multiple=true` is explicitly supplied. The optional `icon` accepts `sun`, `moon`, `system`, `code`, `edit`, `folder`, `check` or `warning`.

## Action suggestions skill

Enable or disable **Propositions d’actions / Action suggestions** in **Settings → Skills**. The skill is initially enabled. It exposes `suggest_actions` in the native desktop Agent conversation with direct model providers. OpenCode, ACP, plain Chat mode, child agents and hosted CLI/server sessions do not expose this desktop action tool.

A proposal is a persistent chat card with 1–4 buttons. It returns immediately without executing work, granting permission or pretending that the user has answered. The model must finish its response and wait for a real user click. The user may dismiss the proposal.

| Kind | Result after the user clicks |
| --- | --- |
| `continue_here` | Add the prompt to the current conversation and start it when idle, or after the current turn finishes successfully. |
| `launch_agent` | Create a dedicated conversation and request one agent. Uses a direct non-composite provider and the configured agent runtime. |
| `new_chat` | Create a dedicated conversation and send the prompt. |
| `enqueue` | Append the prompt to this conversation’s existing queue. If idle, it waits for Resume queue; an active successful run processes it normally. |

Each proposal needs `title` and `actions`; `description` is optional. Each action needs `kind`, `label` and a self-contained `prompt`; `description` is optional. Include the observed bug, paths and task scope in the prompt because the entire parent transcript is not copied. The host validates lengths and the closed set of action kinds. All prompts are readable under **Action prompts** before selection.

New conversations inherit the project, attached resources, sandbox and Plan/Proposition/Execution mode. Launching an agent requests one agent explicitly; other tool approvals and project restrictions still apply. Queued tasks use current settings when they start. Showing a proposal does not automatically execute it. Selecting one alternative consumes the whole proposal atomically with queue insertion, preventing replay after reopening or double clicks. Failed generations retain queued work for inspection or manual resume. To request another alternative, ask for a new proposal.

Cards and the chosen action survive reopening and appear as readable text in conversation exports. Disabling the skill removes its model tool and rejects pending action clicks; dismissal remains available. This system does not grant access to operating-system commands or arbitrary application APIs.
