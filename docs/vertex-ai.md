# Vertex AI / Google Cloud

Monolith can call Gemini through Vertex AI, now also documented by Google as Gemini Enterprise Agent Platform. This connection uses your Google Cloud project and billing account. It is separate from the Gemini API connection authenticated by an AI Studio key.

## Choose authentication

Desktop settings, initial setup, the CLI wizard and server administration offer:

| Mode | Required configuration | Transport |
| --- | --- | --- |
| **Google Cloud ADC** (default) | Project ID, location, credentials on the calling machine | OpenAI-compatible Vertex endpoint |
| **Google Cloud API key · project** | Project ID, location, a key authorized for Vertex AI with the required project IAM access | Native Gemini API |
| **Google Cloud API key · Express** | A key created for Vertex AI Express | Native global Gemini API; no project/location fields |

For API-key authentication, select the appropriate mode and paste the **key string** into the masked API-key field. Existing keys stay unchanged when you leave that field blank. Keys are encrypted in Monolith storage and sent in the `x-goog-api-key` header, never in URLs. No ADC login is used in either key mode.

A Google Cloud key must be accepted by Vertex AI; an arbitrary key created for another API or an AI Studio-only key is not sufficient. For standard projects, check the key restrictions and, where required, its service-account binding and IAM access. Express requires its own Express key. Google determines whether billing credits apply.

API keys use a native adapter because Google's OpenAI-compatible endpoint documents OAuth authentication. The adapter preserves messages, base64/HTTPS images, tool calls and their thought signatures, streamed reasoning and usage. Native key mode supports **Gemini chat models**. The same server relay works for both streaming and non-streaming chat clients.

**Express has no model-list endpoint.** Enter an exact model ID manually, for example `google/gemini-2.5-flash`; the catalogue button explains this limitation instead of reporting a successful connection or inventing a list. For project keys, catalogue discovery may require additional access; manual model entry remains available.

## Check a connection

In Desktop settings, **Test connection** displays progress and the result directly below the connection buttons. Success, errors and timeouts have distinct states.

For all Vertex modes, the button calls **countTokens** with a short fixed connection-check text and the selected model, using the configured authentication. It does not submit conversation contents or generate a response. Success confirms access to that counting endpoint; it does not verify generation permissions, credit eligibility or remaining balance.

**Import models** remains a separate catalogue operation. Express has no catalogue: enter the model ID manually, then use **Test connection**.

Google Cloud key strings are opaque and can contain punctuation, including a dot. Copy the complete string into the masked field; spaces and characters that cannot safely form an HTTP header are rejected. No real key should be copied into source code, documentation or logs.

## One-time ADC setup

1. Create or select a Google Cloud project and associate it with the billing account that owns your credits. Use the **project ID**, not its display name.
2. Enable the Vertex AI API (`aiplatform.googleapis.com`). Grant the calling identity the required Vertex AI permissions, typically **Vertex AI User**. When using user ADC and a quota project, **Service Usage Consumer** may also be required.
3. Install the [Google Cloud CLI](https://cloud.google.com/sdk/docs/install) if you use a personal account, then run:

   ```text
   gcloud auth application-default login
   ```

   Finish Google's sign-in in your browser. This creates Application Default Credentials (ADC) for your operating-system account. Ordinary `gcloud auth login` alone does not configure ADC.

4. In Monolith Desktop, open **Settings → Providers → Add provider → Vertex AI · Google Cloud**. Enter the project ID and a supported location; `global` is the default. The URL is built automatically. There is no AI Studio key to enter.
5. Import the catalogue or enter an exact model ID, for example `google/gemini-4-argon`. Bare Gemini IDs are normalized to `google/…`. Choose the models to enable, save, and select this connection in chat.

The CLI connection wizard offers the same authentication modes; ADC and project keys ask for the project and location. Authentication is resolved by the official Google .NET library; no local proxy or Python installation is needed. ADC also supports `GOOGLE_APPLICATION_CREDENTIALS` and supported attached workload identities. Configure credentials under the account running Monolith, using Google's supported setup; do not paste a credential JSON into an API-key field.

## ADC tokens and billing

In ADC mode, the Google authentication library obtains and refreshes short-lived access tokens before requests. Tokens are not saved in Monolith's provider database. After replacing an existing ADC sign-in, restart Monolith so it loads the new identity. On another computer, configure ADC again rather than copying credentials with the application.

Requests target the configured project's OpenAI-compatible endpoint and explicitly identify that project as the quota/billing project. For `global`:

```text
https://aiplatform.googleapis.com/v1/projects/PROJECT_ID/locations/global/endpoints/openapi
```

Regional locations use `LOCATION-aiplatform.googleapis.com` and the same location in the path. Only matching Google Cloud HTTPS destinations are accepted for automatic authentication.

**Credits are not an application setting or a spending cap.** Google decides whether a particular call is eligible for your credits. Check the linked billing account, remaining credits, expiry, restrictions and usage in the Google Cloud console. A successful catalogue request does not verify inference access, model availability in the chosen location, quota, credit eligibility or remaining balance.

The catalogue lists Google publisher models and filters for Gemini chat IDs. It is not an account-specific entitlement list. If catalogue access is unavailable, enter the documented ID manually; no fabricated list is substituted for an API error. Context limits use published metadata when present and the normal labelled fallback otherwise. Verify the token limit and image capability before enabling a model.

The existing streaming, tool calling, context handling and retry settings apply to Vertex requests. The image-generation tool does not use this connection; Vertex image APIs require a separate integration. This integration covers Desktop, CLI and the .NET server; the standalone Flutter client does not obtain Google ADC credentials from a desktop machine.

## Shared server providers

In server administration, choose **Vertex AI · Google Cloud**, select the authentication mode and configure the offered models. For API-key modes, enter the key in the masked field; it remains encrypted on the server and is never sent to clients. For ADC, enter the project/location and configure ADC **on the server machine, for the server process identity**. A Windows service, container or remote server does not inherit your desktop user's Google sign-in. Use an appropriate service/workload identity for deployment.

Clients receive the usual Monolith relay configuration and never receive the server's Google credentials. In ADC mode, the server refreshes tokens as needed for upstream requests. Existing manually configured bearer-token connections continue to use their stored token until you explicitly replace that authentication with ADC.

## Errors

- Missing credentials or an expired sign-in: configure ADC and restart Monolith after replacing an existing identity.
- HTTP 401/403: check the selected authentication mode, key type/restrictions, Google identity, project IAM permissions, API activation and quota-project access. Do not put an OAuth access token into a Google API-key field.
- HTTP 404 or model unavailable: check the exact ID and supported location; publisher catalogue visibility is not proof of inference access.
- Quota or billing errors: inspect project quotas and billing/credit eligibility in Google Cloud.
- A model that requires reasoning may refuse **Off**. Choose an available effort level rather than expecting hidden reasoning to disable it.

## References

- [Vertex API keys](https://cloud.google.com/vertex-ai/generative-ai/docs/start/api-keys)
- [Express REST API](https://cloud.google.com/vertex-ai/generative-ai/docs/reference/express-mode/api-reference)
- [Google Cloud API keys and authorization keys](https://cloud.google.com/docs/authentication/api-keys)
- [OpenAI compatibility](https://cloud.google.com/vertex-ai/generative-ai/docs/start/openai)
- [Authentication and token refresh](https://cloud.google.com/vertex-ai/generative-ai/docs/migrate/openai/auth-and-credentials)
- [Local ADC setup](https://cloud.google.com/docs/authentication/set-up-adc-local-dev-environment)
- [Google Cloud Free Trial conditions](https://cloud.google.com/free/docs/free-cloud-features)
