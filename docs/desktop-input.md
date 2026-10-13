# Desktop keyboard and mouse modes

Configure each skill separately in **Settings → Skills → Keyboard control / Mouse control**.

| Mode | Behavior |
| --- | --- |
| Automatic (default) | Windows UI Automation first. On macOS, use the existing Legacy backend. |
| UI Automation / API | Windows semantic control actions only. Unsupported operations are reported without physical input. |
| Legacy | Existing physical keyboard, mouse and foreground window interaction. |

**Allow AI to choose a mode** is enabled by default. In Automatic mode the AI may choose `uia` or `legacy` per tool call. A fixed mode cannot be overridden. Turning this option off makes the configured strategy mandatory.

**Automatic fallback to Legacy** is disabled by default. When enabled in Automatic mode on Windows, a capability failure detected before any mutation can trigger a separately authorized Legacy call. Physical actions require explicit, verified coordinates or keys. An execution failure or timeout never triggers an automatic replay. Legacy is temporarily blocked while a timed-out UI Automation provider is still running.

## Windows control inspection

With either input skill enabled, `desktop_inspect_ui` first lists windows when called without `window_id`. Then supply an exact window ID to inspect a bounded control tree and get opaque `element_id` values and supported actions. IDs expire after ten minutes or a new inspection of that window; refresh them when controls change. For a large or truncated tree, use `parent_element_id` from the previous inspection to inspect that container subtree. Each action checks that its control still belongs to the requested window.

- Keyboard: `type` appends to the complete field value; `set_value` replaces the complete value. Neither simulates a caret or preserves text selection. Password values are never read; only explicit `set_value` can target a password field.
- Mouse: native invoke/click, toggle, selection, scrolling, scrolling into view, expansion and collapse when exposed by the provider.
- Shortcuts, right/double clicks, hovering and dragging require Legacy.
- UI Automation does not inject physical mouse/keyboard events. Applications may still open dialogs or change focus themselves.

Use existing browser DOM or application API tools when exposed. This release does not add arbitrary application-specific API adapters. An application's own UI Automation provider exposes the available semantic actions.

## Permissions and platforms

All modes retain conversation permissions, resource boundaries, execution-mode restrictions and sandbox restrictions. Existing project rules for `desktop|mouse` and `desktop|keyboard` also apply to their mode-specific scopes. Saved grants distinguish UI Automation and Legacy. Results report the actual mode and fallback reason. Inspection transmits control labels and window titles as untrusted data, without reading control values.

Windows UI Automation runs on one dedicated MTA thread with native connection/transaction limits and a ten-second caller deadline. Cancellation prevents mutations that have not begun; it cannot forcibly stop an already-running native provider action. After an uncertain result, inspect the target state before retrying.

macOS uses Legacy and requires the OS Accessibility permission. Linux desktop keyboard/mouse control remains unavailable. Browser tools retain their own DOM/MCP behavior and permissions. The CLI retains its existing exclusions for graphical desktop skills.
