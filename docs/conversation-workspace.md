# Conversations, resources and compatibility

These features are shared by WinUI on Windows and the Electron Windows/macOS interface.

## Resources and projects

**Manage project** configures multiple default folders. A conversation inherits them until its resources are customized. The **+** menu can add files or folders and restore project defaults. Dropping files or folders onto the composer adds them to the conversation's scope. A file attached as a source grants access only to that file, not its parent folder. The Images button continues to send images to the model.

Changes apply to the next send. An active generation keeps its configuration snapshot. Git, the explorer, new terminals, source tools and RAG results respect conversation resources. An existing terminal in a detached folder can no longer launch commands. The sandbox retains its folder-copy constraints and requires a new conversation when its roots change.

`AGENTS.md`, `Agent.md` and `AGENT.md` load automatically from attached folders and subfolders. Instructions remain subordinate to the user's request, Plan mode and permissions.

## permission.json

Place this file in a default project folder, then use **Manage project → Read/Import permission.json**. Review the rules and save/apply them. The application keeps a snapshot in SQLite: editing the file, including by an agent, does not change permissions without a new import. New generations use that snapshot.

```json
{
  "permissions": {
    "terminal": "ask",
    "python": "ask",
    "desktop": "deny",
    "rag-api": "allow",
    "source-patch": "ask",
    "write_source": "deny",
    "edit_source": "deny"
  }
}
```

- Values: `allow`, `ask`, `deny`.
- A key can name a tool, a request family (`terminal`, `python`, `desktop`, `rag-api`, `source-patch`) or an exact scope such as `desktop|mouse`. `*` is the default rule.
- A denial by tool name is checked before execution, including in OhMyHarness subagents. Allowing a tool name does not bypass internal checks, a disabled skill, Plan mode or the sandbox.
- For additional requests: exact scope, then family, then `*`. Global **Deny all** retains priority; `deny` remains a denial even with automatic approval. `ask` requires a dialog even if a permanent grant exists.
- If multiple files define the same key, `deny` wins. Maximum size: 32 KB per file. Removing rules restores the usual policy.
- Tools run by the OpenCode server remain subject to its rules; requests relayed to OhMyHarness pass through the project profile.

## Resume and fork

Hovering over a user message or completed final answer reveals **Message actions**, with **Create fork** and **Resume here**. A fork creates a new conversation through the selected message, including images, conversation settings and resources. Answers containing intermediate tool calls cannot be starting points.

Resuming requires stopping generation and confirmation: full history is first copied into a **Backup** conversation, then the current conversation is rewound to the chosen message. Its queue and OpenCode binding are reset. Previously compacted messages become usable in the restored context. Send another instruction to continue. This does not restore project files or browser/terminal state.

## Display

- The desktop sidebar shows independently expandable project folders. Several folders can remain open at once; clicking a folder only collapses or reopens it, and clicking a conversation selects it. Folder expansion is retained during the application session. The filter icon reveals conversation search; the folder-plus action creates a project, and the plus beside a project creates a chat there. Right-click a project to manage it, including its icon and color (12 icon choices and a color palette with live preview). The application logo and name remain above Projects; the notification bell sits immediately left of the search icon.
- Right-click a conversation and choose **Pin / Unpin** to add or remove its shortcut in **Pinned**, above Projects. The conversation remains in its original project or archive. Pins are saved in SQLite and survive application restarts.
- Parent project badges count running chats (**…**), unread completed replies (**✓**) and chats needing attention (**!**). Opening a chat clears its unread completion or error marker; a pending question or permission request remains marked until answered. Badge counts work independently of notification and sound preferences and are retained for the current application session.
- The version beside the application name is a theme-aware gradient chip. Click it to open the complete bundled changelog in a scrollable dialog; no internet connection is needed.
- **Conversations** contains chats without a user-created project. Favorite chats stay at the top; compact ages indicate their latest message submission. Archives retain a separate, bounded scroll area.
- During AI title generation, a rotating indicator and **Nommage… / Naming…** appear in the chat row and current title.
- Hovering a conversation keeps the row dimensions stable. Hovering a completed response reveals its generation duration and local completion time (`HH:mm:ss`); the timestamp tooltip includes the date. Completion times are saved for new responses and preserved in forks. Older responses without a recorded timestamp retain their duration only.

### Notifications

**Settings → Notifications** enables completed-chat and action-required events independently. The bell opens unread notifications and takes you to the corresponding chat; the same bell appears on affected conversation rows. Reading a completed conversation clears its notification. Notifications for pending questions or permissions remain until the request is answered. Notifications are retained for the current application session, up to 100 chats.

Sound can be enabled separately, with nine tones (Soft, Bell, Alert, Chime, Glass, Marimba, Digital, Success and Water drop), volume and a preview button. The Notifications settings tab uses an audio icon. Sound playback is implemented on Windows. These preferences are saved with the portable application settings.

[Persistent memory](memory.md) has a Settings tab: two skill levels (conversation and shared), Project/General/User categories and indexed SQLite search.

- Conversations show a skeleton during background reads, then the 24 latest messages. Earlier pages load while preserving reading position. Full history remains in the database and is used by the model; only images from displayed pages load into the interface.
- Switching conversations cancels the previous visual load without stopping agents. Drafts are retained. Input and navigation remain available; sending waits until the selected conversation is ready.
- Settings opens immediately with a loading indicator. Git and Files have their own progress bar; an old result cannot replace the new conversation's result.
- The collapsed TODO shows `current step / total · step name`. **×** hides it for the conversation; **+ → Show task list** reopens it.
- Information, tasks and queue use consistent surfaces. Queued messages retain **Delete / Edit / Steer**.
- Action status has a subtle glow. A right-pointing arrow means collapsed; a downward arrow means expanded.
- Git offers **Before / after** and **Combined diff** for the same selected file, from HEAD to the working directory.
- In **General**, below reasoning: **Open and focus the latest AI tool**. Disabled by default; applies only to the visible conversation.

## HTTP and provider errors

OpenAI v1-compatible providers accept `http://` and `https://` URLs, including remote HTTP servers. Credentials embedded in URLs are still rejected. HTTP transmits without TLS encryption; use HTTPS when the server offers it.

API errors show provider details with the request key masked. Some HTTP 400/422 errors identifying an incompatible capability trigger an adapted retry: `reasoning_effort`, streaming statistics, images, tools or DeepSeek reasoning history. Maximum four retries after the initial request. Authentication and unrecognized errors are not automatically retried.

Degraded mode is visible and saved with the answer. Original history and images remain in the database. If tools are refused, the response becomes text-only and cannot act. If vision is unavailable, an explicit marker replaces the image in the request; enable/configure **Bypass image AI** and disable the main model's image capability to delegate analysis. No other paid provider is selected automatically.

The capability fallback above does not replay a request after a successful stream has started. HTTP 400 alone does not establish that a key or a particular capability is responsible.

**Settings → General → Automatic retry** separately handles transient network errors, HTTP 408/429/5xx and interrupted model streams. Choose 0–10 additional attempts and a 1–300 second delay; defaults are enabled, three retries and five seconds. Retry status shows the attempt and delay. Stopping generation cancels the wait. Interrupted text is replaced by the new attempt, and completed tool calls are not replayed. Invalid keys, ordinary HTTP 400 errors and tool execution errors do not trigger this mechanism.

For OpenCode, read-only polling can retry; a prompt submission retries only an explicit HTTP 429 response. Ambiguous submission failures are left visible because the remote server may already be executing the request. Model benchmarks disable retries so timing and scores reflect the original attempt.
