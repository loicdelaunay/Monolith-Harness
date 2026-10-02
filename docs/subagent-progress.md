# Subagent task progress

Each subagent uses **todowrite** to announce its own concrete task list before working. The list belongs to that agent and does not replace the parent conversation's tasks. Agents keep completed work in the list, update the current task and mark abandoned tasks cancelled. They can revise their plans as they discover additional work.

The GUI displays **completed / declared tasks**, a progress bar and the current task in both the conversation and sidebar. Click a subagent to see its plan and transcript. **Parent conversation** stays in an accented, fixed header above the scrolling content. The CLI displays the same declared task counts and progress.

Completion percentage counts only tasks marked completed. Cancelled tasks are shown separately; a failed, interrupted or finished agent does not automatically mark every task complete. Before a plan is provided, the running indicator remains indeterminate. Older transcripts without plans have no invented task counts. Plans are saved with the transcript and survive reopening the application.

The internal model-step budget remains configurable in agent settings. It limits requests and is not used as the progress denominator. Announcing or updating a plan can use additional model steps and tokens. With OpenCode, enabled native tools allow its task list to be polled into the corresponding agent's record; no plan is invented when native task tools are unavailable.
