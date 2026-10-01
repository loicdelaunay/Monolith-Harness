# File preview

**Tools → Preview** opens a dedicated page in the right workspace pane. Click a compatible local file link in a message or tool result, an attached image, or a TXT / Markdown attachment. The file context menu also offers **Preview** alongside **Open**, **Open in browser** and **Open containing folder**. Those explicit actions retain their existing behavior. Other file types continue using the embedded browser.

## Views

- **Preview** renders Markdown headings, lists, tables, quotes and code; TXT displays selectable, wrapped text. Images have Fit / 50% / 100% / 200% controls. The pane can be enlarged using the existing tools-pane action.
- **Raw** displays decoded file text without Markdown formatting. For images, it shows name, MIME type, dimensions, byte size and paginated hexadecimal bytes.
- **Open a file**, at the bottom right, selects a local file. **Refresh** reloads it. **Copy** copies the entire decoded text, or the displayed image metadata and hexadecimal page. The file-actions menu is available for files on disk; chat attachments do not have a disk location.

## Supported content

Local files: `.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.bmp`, `.ico`, `.txt`, `.md`, `.markdown`. TXT and Markdown attachments may also be recognized by their MIME type. GIF displays a still frame. SVG, HTML, PDF and other formats retain the browser route.

Text: up to 2 MiB; UTF-8, UTF-16 / UTF-32 with a BOM, and Windows-1252 fallback for older text files. Each view displays pages of about 40,000 characters. The full text remains available through pagination and Copy. Pages favor paragraph or line boundaries; Markdown constructs spanning a page boundary may render partially.

Images: up to 32 MiB and 40 megapixels, with a 16,384-pixel maximum dimension. Display copies are resized to a maximum 2,048-pixel edge, or 1,024 pixels for embedded Markdown images; Raw retains the original bytes and dimensions. Zoom enlarges that display copy. Binary pages contain 1,024 bytes.

Markdown: at most 400 rendered blocks and 12 embedded images per page. Extremely complex blocks or very long code blocks use a plain-text fallback; if a page reaches the block limit, its remaining content is available in Raw. This preserves responsiveness with generated or unusually large documents.

## Local access and navigation

File reading uses the application's local-preview path protections, including protected paths and symbolic-link checks. Relative chat links resolve against the sources associated with their message. Links within a Markdown preview resolve against its file directory first.

Automatically embedded Markdown images must stay inside the document's directory and pass the same path protections. Remote images are not downloaded; HTML and scripts are not executed. Opening a preview does not submit its content to an AI provider.

Reads, decoding and parsing run on worker threads. Rendering yields between blocks, and navigating to another conversation cancels pending loads and clears the panel. A result from an older request cannot replace the currently selected preview.
