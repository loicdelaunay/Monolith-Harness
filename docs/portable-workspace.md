# Portable workspaces

GUI and CLI keep application-managed folders together in `workspace/`, beside the actual launcher. Selecting another SQLite profile with CLI `--database` places its workspace beside that database. Copy the complete portable folder after closing all instances to retain conversations and resources.

```text
MonolithHarness.exe                 # MonolithHarness on Linux
database.sqlite
MCP.json
workspace/
  conversations/chat-41/            # Files created for a folderless conversation
  skills/
  model/                            # Imported local models and inference engines
  models/                           # RAG embedding resources
  assets/chat-41/                   # Drawing scenes and their exports
  images/chat-41/
  scripts/python/chat-41/
  runtimes/python/
  sandboxes/
  logs/
  temp/
```

Browser profiles, branding, terminal profiles, exported fonts and other managed folders also live in `workspace/`. Linux keeps `.linux-key` beside the database; retain it when copying that profile.

## Files without a project folder

A saved conversation with no attached folder gets `workspace/conversations/chat-<id>/` automatically. File-only attachments remain limited to the individual files and receive this additional directory for generated work. Source tools, file navigation and new terminals use the conversation directory. In the GUI, the resource chip reads **Workspace / Espace de travail** and the source label displays the full path.

Each conversation has its own directory. Restarting the application or returning to the chat reuses it. Attaching a project folder makes that folder the working root; removing the last folder returns to the same conversation workspace. No source folder is added to the shared Conversations project. A configured but missing project folder remains a missing resource until you fix or detach it.

Enable the source-editing or terminal skills when needed. The automatic directory preserves Plan mode and existing approval rules. A terminal or approved script runs with the user's operating-system rights; source-tool scope is not a container sandbox.

Generated files are retained when a conversation is deleted or pruned. Remove unwanted `chat-<id>` folders manually after saving any files you need. Forking or rewinding history does not copy or restore file contents.

## Migration from earlier versions

Startup moves known application-managed folders into `workspace/` and updates stored project/resource references, local-model paths, branding, MCP working paths and path-specific permissions. External project folders and SQLite remain in place. Installed application assets and native libraries remain available in development builds.

If a destination already exists, both folders are preserved. The legacy folder moves as a complete directory into `workspace/legacy-imports/<unique-id>/`; saved references continue to resolve to that copy. Global skills merge unique entries into `workspace/skills/`, retaining conflicting entries in the legacy import. No existing files are overwritten.

`workspace/.storage-layout.json` records completed or pending moves and keeps older file links usable. Retain that file when copying the portable profile. A folder that is temporarily locked remains usable at its old location and migration retries on a later startup. Close other application instances before updating. Back up the complete portable folder before reverting to an older release that predates this layout.
