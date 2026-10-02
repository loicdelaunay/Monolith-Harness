# Local models and image generation (1.47.0, beta)

## Configure a local provider

1. Open **Settings → Providers → Add → Local**.
2. Keep the default `model/` directory next to the executable, type another writable location or choose **Browse…** beside the directory field. Cancelling the native folder picker keeps the previous path. Imported files are copied there by default; disabling the copy switch keeps a reference to the original file.
3. Import a file, or open **Hugging Face** in its independent, resizable window. Search and choose Chat or Images in the top bar. Models appear on the left; selecting one shows its repository statistics, licence, files/quantizations, support estimate and model card on the right. Compatible files appear first; unsupported components remain visible with their reasons. The panes stack in smaller windows. Choose **Download and import** at the bottom right; cancelling keeps the browser open, while closing stops the transfer. Protected repositories require a read token and accepting the author's conditions on Hugging Face. Tokens use the existing protected credential storage.
4. Select an imported model and choose **Prepare / load**. This downloads an official inference engine, checks its published SHA-256 and installs it under `model/.runtime/`. Chat models are loaded into a private local server. Image engines are prepared here; image checkpoints are loaded when generating, then released.
5. Save settings. Local chat models become available in the regular model selector. Image-only providers remain in settings and in the image skill selector. Removing a model from the list keeps its file. Unload releases idle chat processes; active requests prevent unloading.

Windows/Linux x64 engines require AVX2; automatic installation is also available for Apple Silicon when an upstream binary exists. Automatic backend selection uses Vulkan when a graphics adapter is detected, CPU otherwise, and Metal on macOS. Select CPU if the Vulkan driver is unavailable. The advanced fields accept an existing `llama-server` or `sd-cli` executable. These engines run with the user's permissions.

## Supported files

| Use | This beta accepts | Requires another configuration |
| --- | --- | --- |
| Chat | Standalone GGUF, format version 2 or 3; architecture/chat template confirmed by llama.cpp on load | Split GGUF sets, vision projectors, Transformers directories, ONNX and adapters |
| Image | Full Safetensors checkpoints containing the SD/SDXL UNet, text encoder and VAE; family detected from tensor names | Diffusers component directories, quantized diffusion GGUF requiring separate components, LoRA, Flux/SD3 and other model families, Pickle/CKPT |

Hugging Face model cards and file names are hints, not proof of compatibility. The importer checks file headers and full-checkpoint components. It does not download or execute model-repository scripts. Downloads use the repository revision returned by the Hub and verify size and the LFS SHA-256 when provided. Cancelled transfers remove their own partial file. Existing model files are never overwritten automatically.

## Support estimates

The browser's **Your machine** section shows installed/free RAM, CPU threads and graphics adapters. NVIDIA VRAM is read from `nvidia-smi` when available; Windows adapter memory is labelled as an estimate because it can be incomplete. Unknown hardware remains unknown.

For chat, the conservative budget is weights × 1.15 plus roughly 1 GiB per 8,192 context tokens. For images, it is weights × 1.25 plus 2 GiB (SD 1/2) or 5 GiB (SDXL), with that overhead scaled up above 1024×1024 at generation time. These are rough budgets, not architecture-specific KV-cache calculations. Resolution, batches, drivers, concurrent local models and other applications can change actual consumption. A RAM budget above 85% of installed memory is marked unsupported; a budget exceeding currently free memory is marked “check first”. Insufficient VRAM can require CPU or partial GPU computation; no token rate is promised.

Loading confirms the actual runtime's architecture and driver support. A smaller quantization/context often reduces memory. GPU rendering settings affect application rendering, not the inference backend selected here.

When a detected dedicated GPU's total VRAM can contain the estimated budget and the backend permits GPU computation, the estimate also considers full GPU offload rather than requiring all weights to fit in RAM. At least 4 GiB of system RAM is still required. Total VRAM is not free VRAM, and CPU/Vulkan selection and driver support still need to match.

## Image generation skill

1. Enable **Settings → Skills → Image generation · Beta**.
2. Choose a provider and an image model. Local choices come from imported image checkpoints. Remote providers must expose `POST images/generations`; a provider's chat model catalogue does not imply image support. Enter the exact image model identifier if needed.
3. Choose dimensions (multiples of 64, 256–2048) and local sampling steps (1–100). Remote providers have their own accepted sizes; for example, not every endpoint accepts 512×512. SD 1.x commonly uses smaller images than SDXL.
4. Ask the chat model to generate an image. It calls `generate_image` with a description and, optionally, a negative prompt for local models. The selected image model is independent of the chat model.

The tool requests the conversation's configured permission. Remote prompts can incur charges. Successful output is validated as PNG/JPEG/WebP, attached to the tool result and saved in `images/chat-<id>/`; compatible file links use Preview. One image per call, a 15-minute timeout and cancellation are supported. Existing-image editing and multi-component image pipelines are not included in this beta. Plan and sandbox modes exclude image generation. OpenCode sessions retain their native tools and cannot call this application tool.

The shared engine exposes the skill to GUI and CLI sessions. The GUI provides the Hugging Face search and image configuration interface. The CLI's provider wizard also offers **Local · GGUF · Beta** to import and prepare an existing chat model. Portable settings and files must be present next to whichever executable uses them.

## Implementation references

- [llama.cpp server and tool-call API](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md)
- [stable-diffusion.cpp checkpoints, engines and CLI](https://github.com/leejet/stable-diffusion.cpp)
- [Hugging Face Hub API](https://huggingface.co/docs/hub/api)

Compilation, publication and browser checks are separate from inference validation. The GUI checks use small offline fixture files; loading real model weights and image generation still depend on the selected model, runtime and hardware.
