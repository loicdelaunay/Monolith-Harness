# Cloud providers (1.42.0)

## Setup

In **Settings → Providers → Add provider**, choose OpenRouter, Groq, Google Gemini, Mistral, Z.ai or NVIDIA NIM. The official API URL is filled in. Use **Get an API key**, enter your key, then import models or enter an exact model identifier. Save the connection and choose it in the chat. You can create several connections for the same service, each with its own protected key and model selection.

The CLI connection wizard offers the same six presets. It supports automatic discovery and manual identifiers; credentials are stored only after the final confirmation. API keys are required for these services, including their free offers.

| Provider | Default API base URL | Access |
| --- | --- | --- |
| OpenRouter | `https://openrouter.ai/api/v1` | Free models with quotas and paid models |
| Groq | `https://api.groq.com/openai/v1` | Limited free access or paid usage |
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai` | Free quota on selected models; paid models and features |
| Mistral | `https://api.mistral.ai/v1` | Limited free mode and paid usage |
| Z.ai | `https://api.z.ai/api/paas/v4` | Selected Flash models free; other models and images paid |
| NVIDIA NIM | `https://integrate.api.nvidia.com/v1` | Free prototyping under NVIDIA's conditions and limits |

Free access is not unlimited. The account, model and provider determine quotas and billing; Monolith does not bypass them. A catalogue can list models that still require account access or credit. Discovery confirms a catalogue request, not a successful generation with every model.

## Models and reasoning

Model lists belong to individual connections. Refreshing uses that connection's URL and key. Explicitly non-chat models are omitted when the catalogue publishes those capabilities. Gemini identifiers with the `models/` prefix are normalized for chat requests. Choose the context limit and vision checkbox according to the selected model; these settings are editable.

Z.ai does not document a general model-list endpoint. Its supplied examples are labelled **access not verified**. If automatic discovery is unavailable, enter the model identifier manually and save. A failed list request is never reported as a successful connection. The default example `glm-4.7-flash` is from the documented free catalogue; your key, quota and availability still apply. The default URL is for the general platform API. A Coding Plan uses a separate endpoint and its own conditions.

OpenRouter's reasoning effort is sent with its `reasoning` parameter. Its reasoning blocks and Gemini's opaque thought signatures are retained across tool calls. Mistral's thinking chunks are rendered as reasoning while final text stays in the answer. Groq usage metadata is recognized. GLM maps explicit thinking selection to enabled/disabled; its effort is model-managed and some models impose thinking. Compatibility notices explain those differences and any automatic fallback.

## Image generation

Choose Gemini or Z.ai in **Settings → Skills → Image generation**, with an image model available to your account. Chat-model discovery alone does not grant image access. Gemini uses the compatible image endpoint and base64 output. Z.ai provides documented examples `cogview-4-250304` and `glm-image`, labelled as unverified. GLM-Image needs at least 1024 pixels per side; CogView needs at least 512 and no more than 2,097,152 pixels in total. The existing skill enforces multiples of 64 and a maximum of 2048 per side.

OpenRouter, Groq, Mistral and NVIDIA NIM are chat presets here. Their image services, if available, use different APIs and are not exposed by this image-skill connector. Generic compatible and Local image providers remain available. Remote image generation retains the chat's permissions and can incur charges.

## Official references

- [OpenRouter quickstart](https://openrouter.ai/docs/quickstart) and [reasoning](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens)
- [Groq compatibility](https://console.groq.com/docs/openai)
- [Gemini compatibility, tools and images](https://ai.google.dev/gemini-api/docs/openai)
- [Mistral models](https://docs.mistral.ai/api/endpoint/models) and [streamed reasoning](https://docs.mistral.ai/studio/conversations/reasoning)
- [Z.ai API](https://docs.z.ai/api-reference/introduction), [thinking](https://docs.z.ai/guides/capabilities/thinking-mode) and [images](https://docs.z.ai/api-reference/image/generate-image)
- [NVIDIA API quickstart](https://docs.api.nvidia.com/nim/re/docs/api-quickstart)

Implementation is compiled locally for GUI and CLI. Real account authentication, model generations and automated tests have not been run for this update.
