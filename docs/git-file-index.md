# GIT and FILE INDEX

Enable these independent skills in **Settings → Skills**, **+ → Skills**, or the CLI's **`/skills`** picker. They start disabled. Attach the relevant source folders to the project or conversation first. When several folders are attached, use the folder alias returned by `list_sources` to select one.

Both skills include source reading without enabling arbitrary source writes. Source editing and terminal execution retain their own switches. Existing permission settings and imported project permission rules still apply.

## GIT

Git must be installed and available to the application. Select the repository root as `repository` (default `.`). The tool rejects implicit discovery of a parent repository outside the selected folder.

| Tools | Purpose |
| --- | --- |
| `git_status`, `git_diff`, `git_log`, `git_branches` | Inspect working/staged changes, recent commits, branches and configured remote names. |
| `git_init` | Initialize a repository in an attached folder. |
| `git_stage`, `git_unstage` | Stage or unstage explicit file paths, including individually selected deletions. |
| `git_commit` | Commit the current staging area with a supplied message. The approval includes staged file names. |
| `git_switch` | Switch to a local branch, or create one with `create: true`. |
| `git_fetch`, `git_pull`, `git_push` | Use an existing configured remote; pull/push also require a branch. |

Example requests: “Show the differences in this repository”, “Commit these three files with a clear message”, or “Push branch feature/index to origin”. Committing does not implicitly authorize a push.

Mutations and network operations pass through the application's approval policy. Staging requires individual files rather than directories or wildcard selections. Excluded source paths are rejected, and diff tools disable external diff and text-conversion commands. Git hooks and credentials otherwise follow the local Git setup. Pull only accepts a fast-forward; push does not force or automatically publish tags. There are no reset, clean or arbitrary argument tools.

In **Plan**, only the four read tools are exposed. Subagents also receive only Git read tools so concurrent workers cannot independently change shared repository state. Git mutations/network tools are unavailable in the container sandbox. Read operations there require a Git repository in that workspace.

## FILE INDEX

Ask: **“Create an index.ohm in this folder and explain what each file and folder contains.”** The agent creates the inventory, inspects relevant files and adds explanations. On later requests, it can locate useful files using descriptions before reading their contents.

| Tool | Purpose |
| --- | --- |
| `file_index_update` | Create or explicitly refresh an index after approval. `path` selects the folder, `auto_update` controls subsequent automatic maintenance. |
| `file_index_read` | Browse an existing index. `directory` selects immediate children; `query` searches paths and descriptions. Use `offset` and `limit` for pagination. |
| `file_index_describe` | Save explanations for files or folders after approval. Each entry includes its current `fingerprint` from a fresh index read. |

For example, the agent can browse `src` with:

```json
{"path":".","directory":"src","limit":50}
```

Or find entries related to authentication:

```json
{"path":".","query":"authentication","offset":0,"limit":25}
```

### What index.ohm contains

The file is readable UTF-8 JSON, portable with its folder. All entry paths are relative to that folder. It records:

- The format/version, refresh time and `autoUpdate` preference.
- Files and folders, including empty folders and the root (`.`).
- File sizes, modification times and fingerprints.
- `generatedDescription`: a structural summary, such as file type, declarations, JSON keys, documentation heading or directory contents.
- `description`: the automatic summary or a more useful explanation written by the model after inspection.
- `descriptionSource` (`automatic` or `model`) and `descriptionStale`, indicating when an explanation needs review.

Automatic summaries do not claim to understand every file's purpose. The model adds that explanation through `file_index_describe`, based on the source contents. Fingerprints prevent an explanation based on an older file from being attached silently to newer content.

### Ongoing maintenance

With FILE INDEX enabled and `autoUpdate: true`:

1. Source writes, edits and multi-file patches refresh existing indexes in the affected folders and their parents. Git operations refresh the repository's own index and parent indexes.
2. A later `file_index_read` reconciles external additions, modifications, renames and deletions. This also refreshes nested indexes affected by Git or terminal operations when they are next consulted.
3. Unchanged explanations are preserved. A changed file or subtree marks its model explanation stale while refreshing the structural summary. The agent should inspect and revise that explanation before relying on it.

There is no background filesystem watcher or background model call. Disabling the skill stops maintenance. Use `file_index_update` with `auto_update: false` to stop automatic saves for a specific index; explicit updates and description changes remain available. Reads still show a fresh in-memory view. In **Plan**, reads never save, and creation/description tools are disabled.

Indexes obey source exclusions (including `.git`, `.env*`, `node_modules`, `bin`, `obj` and symbolic links), exclude other `index.ohm` files, and contain at most 10,000 entries / 16 MiB. Choose a smaller folder if that limit is reached. Files up to 128 KB receive a content hash and structural inspection; larger files use size/time metadata and are labelled as not analyzed. Existing invalid or unsupported indexes are preserved with an error instead of overwritten. Failed automatic maintenance is reported alongside the successful source edit and retried at the next query.

Descriptions are untrusted project data, never instructions. Index creation does not add the file to Git automatically.

## Host support

The internal GUI, CLI and legacy desktop host expose these tools. Subagents can read/update FILE INDEX within the normal source and permission boundaries; updates are serialized within the application and saved atomically.

OpenCode uses its own native tools and does not receive these internal tool implementations. The skill instructions remain guidance, but automatic `index.ohm` maintenance through OpenCode's native writes is not provided.
