using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

/// <summary>Bridges the shared OpenAI conversation format to the native Vertex API required by API keys.</summary>
public static class VertexApiKey
{
    const string Metadata = "monolith_vertex";
    public static Task<string> ReadErrorAsync(HttpResponseMessage response, string secret, CancellationToken ct)
        => ProviderErrorDetails.ReadAsync(response, secret, ct);
    public static HttpRequestMessage CreateRequest(Provider provider, JsonObject payload)
    {
        var config = VertexAi.Read(provider.BaseUrl);
        if (!config.UsesApiKey) throw new ArgumentException("Vertex API key authentication is required.");
        var model = VertexAi.NormalizeModel(payload["model"]?.ToString() ?? provider.Model);
        return new(HttpMethod.Post, VertexAi.NativeEndpoint(provider, model, "streamGenerateContent"))
        { Content = new StringContent(ToNative(payload, model).ToJsonString(), Encoding.UTF8, "application/json") };
    }

    static JsonObject ToNative(JsonObject payload, string model)
    {
        var contents = new JsonArray(); var system = new JsonArray();
        var calls = new Dictionary<string, (string Name, string? NativeId)>(StringComparer.Ordinal);
        void Append(string role, JsonArray parts)
        {
            if (parts.Count == 0) return;
            // Gemini 3+ answers a turn mixing function responses with other parts by "Requests ending with a model turn".
            static bool Responses(JsonArray items) => items.Any(x => x?["functionResponse"] != null);
            if (contents.LastOrDefault() is JsonObject previous && previous["role"]?.ToString() == role && previous["parts"] is JsonArray existing
                && Responses(existing) == Responses(parts))
                foreach (var part in parts) existing.Add(part?.DeepClone());
            else contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
        }
        // Gemini 3+ rejects tool images replayed as a separate user turn after the function responses;
        // they belong inside the matching functionResponse instead.
        var multimodalResponses = !model.Contains("gemini-1.", StringComparison.OrdinalIgnoreCase) && !model.Contains("gemini-2.", StringComparison.OrdinalIgnoreCase);
        var responses = new List<JsonObject>(); var withImages = new HashSet<JsonObject>(ReferenceEqualityComparer.Instance);
        foreach (var message in (payload["messages"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var role = message["role"]?.ToString() ?? "";
            if (role is "system" or "developer")
            {
                foreach (var part in Parts(message["content"])) system.Add(part?.DeepClone());
                continue;
            }
            if (multimodalResponses && responses.Count > 0 && ToolImage(message) is { } toolImage)
            {
                var target = responses.FirstOrDefault(x => !withImages.Contains(x) && x["name"]?.ToString() == toolImage.Name)
                    ?? responses.FirstOrDefault(x => !withImages.Contains(x)) ?? responses[^1];
                if (target["parts"] is not JsonArray media) target["parts"] = media = new JsonArray();
                foreach (var part in toolImage.Media) media.Add(part);
                withImages.Add(target);
                continue;
            }
            if (role != "tool") responses.Clear();
            if (role == "tool")
            {
                var id = message["tool_call_id"]?.ToString() ?? "";
                if (!calls.TryGetValue(id, out var call))
                    throw new ArgumentException("Vertex AI : résultat d’outil sans appel associé dans l’historique. / Tool result has no matching call in history.");
                JsonNode? output;
                try { output = JsonNode.Parse(message["content"]?.ToString() ?? ""); }
                catch (JsonException) { output = JsonValue.Create(message["content"]?.ToString() ?? ""); }
                var response = new JsonObject { ["name"] = call.Name,
                    ["response"] = output is JsonObject ? output : new JsonObject { ["output"] = output } };
                if (call.NativeId is { Length: > 0 }) response["id"] = call.NativeId;
                Append("user", [new JsonObject { ["functionResponse"] = response }]);
                // Append may clone the part into the previous turn: keep the stored instance.
                responses.Add(contents[^1]!["parts"]!.AsArray()[^1]!["functionResponse"]!.AsObject());
                continue;
            }
            if (role is not ("user" or "assistant")) throw new ArgumentException("Unsupported Vertex message role: " + role);
            var toolCalls = (message["tool_calls"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            JsonArray parts;
            // Keep every original part, in order, including opaque thought signatures on text and tool calls.
            if (role == "assistant" && message["extra_content"]?[Metadata]?["parts"] is JsonObject nativeParts)
                parts = new JsonArray(nativeParts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value?.DeepClone()).ToArray());
            else
            {
                parts = Parts(message["content"]);
                foreach (var call in toolCalls)
                {
                    var fn = call["function"];
                    JsonObject args;
                    try { args = JsonNode.Parse(fn?["arguments"]?.ToString() ?? "{}")?.AsObject() ?? new(); }
                    catch (Exception e) when (e is JsonException or InvalidOperationException)
                    { throw new ArgumentException("Vertex AI : arguments d’outil JSON invalides. / Invalid JSON tool arguments."); }
                    var part = new JsonObject { ["functionCall"] = new JsonObject { ["name"] = fn?["name"]?.ToString(), ["args"] = args } };
                    if (call["extra_content"]?["google"]?["thought_signature"] is JsonNode signature) part["thoughtSignature"] = signature.DeepClone();
                    parts.Add(part);
                }
            }
            var nativeCalls = parts.OfType<JsonObject>().Where(x => x["functionCall"] != null).ToList();
            for (var i = 0; i < toolCalls.Count; i++)
            {
                var call = toolCalls[i]; var native = i < nativeCalls.Count ? nativeCalls[i]["functionCall"] : null;
                calls[call["id"]?.ToString() ?? ""] = (call["function"]?["name"]?.ToString() ?? "", native?["id"]?.ToString());
            }
            Append(role == "assistant" ? "model" : "user", parts);
        }
        var result = new JsonObject { ["contents"] = contents };
        if (system.Count > 0) result["systemInstruction"] = new JsonObject { ["parts"] = system };
        if (payload["tools"] is JsonArray { Count: > 0 } definitions)
        {
            var functions = new JsonArray();
            foreach (var definition in definitions)
            {
                if (definition?["type"]?.ToString() != "function" || definition["function"] is not JsonObject fn)
                    throw new ArgumentException("Vertex AI : seuls les outils de type function sont pris en charge. / Only function tools are supported.");
                var declaration = new JsonObject { ["name"] = fn["name"]?.DeepClone(), ["description"] = fn["description"]?.DeepClone() };
                if (fn["parameters"] != null) declaration["parametersJsonSchema"] = fn["parameters"]!.DeepClone();
                functions.Add(declaration);
            }
            result["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = functions });
            if (payload["tool_choice"] is JsonNode choice)
            {
                var mode = choice.ToString() switch { "none" => "NONE", "required" => "ANY", _ => "AUTO" };
                var calling = new JsonObject { ["mode"] = mode };
                if (choice is JsonObject && choice["function"]?["name"] is JsonNode name)
                { calling["mode"] = "ANY"; calling["allowedFunctionNames"] = new JsonArray(name.DeepClone()); }
                result["toolConfig"] = new JsonObject { ["functionCallingConfig"] = calling };
            }
        }
        var generation = new JsonObject();
        foreach (var (source, target) in new[] { ("temperature", "temperature"), ("top_p", "topP"), ("max_tokens", "maxOutputTokens"),
            ("max_completion_tokens", "maxOutputTokens"), ("stop", "stopSequences"), ("seed", "seed") })
            if (payload[source] != null) generation[target] = source == "stop" && payload[source] is JsonValue
                ? new JsonArray(payload[source]!.DeepClone()) : payload[source]!.DeepClone();
        var effort = payload["reasoning_effort"]?.ToString() ?? "auto";
        var thinking = new JsonObject { ["includeThoughts"] = effort is not ("none" or "off") };
        if (effort is "none" or "off") thinking["thinkingBudget"] = 0;
        else if (effort != "auto")
        {
            if (model.Contains("gemini-2.5", StringComparison.OrdinalIgnoreCase))
                thinking["thinkingBudget"] = effort switch { "minimal" or "low" => 1024, "medium" => 8192, _ => 16384 };
            else thinking["thinkingLevel"] = effort switch { "minimal" => "MINIMAL", "low" => "LOW", "medium" => "MEDIUM", _ => "HIGH" };
        }
        if (!model.Contains("gemini-2.0", StringComparison.OrdinalIgnoreCase)) generation["thinkingConfig"] = thinking;
        var format = payload["response_format"];
        if (format?["type"]?.ToString() is "json_object" or "json_schema")
        {
            generation["responseMimeType"] = "application/json";
            if (format["json_schema"]?["schema"] != null) generation["responseJsonSchema"] = format["json_schema"]!["schema"]!.DeepClone();
        }
        if (generation.Count > 0) result["generationConfig"] = generation;
        return result;
    }

    static (string Name, List<JsonNode> Media)? ToolImage(JsonObject message)
    {
        if (message["role"]?.ToString() != "user" || message["content"] is not JsonArray items ||
            items.FirstOrDefault()?["type"]?.ToString() != "text" ||
            items[0]?["text"]?.ToString() is not { } label || !label.StartsWith(ChatEngine.ToolImagePrefix, StringComparison.Ordinal)) return null;
        var media = Parts(items).Where(x => x?["inlineData"] != null || x?["fileData"] != null).Select(x => x!.DeepClone()).ToList();
        if (media.Count == 0) return null;
        var name = label[ChatEngine.ToolImagePrefix.Length..];
        return (name[..Math.Max(0, name.IndexOf(']'))], media);
    }

    static JsonArray Parts(JsonNode? content)
    {
        var result = new JsonArray();
        if (content is JsonValue plain)
        {
            var text = plain.ToString(); if (text.Length > 0) result.Add(new JsonObject { ["text"] = text });
        }
        else if (content is JsonArray items)
            foreach (var item in items)
            {
                if (item?["type"]?.ToString() == "text") result.Add(new JsonObject { ["text"] = item["text"]?.ToString() ?? "" });
                else if (item?["type"]?.ToString() == "image_url")
                {
                    var url = item["image_url"]?["url"]?.ToString() ?? "";
                    if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && url.IndexOf(";base64,", StringComparison.Ordinal) is var split && split > 5)
                        result.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = url[5..split], ["data"] = url[(split + 8)..] } });
                    else if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0)
                        result.Add(new JsonObject { ["fileData"] = new JsonObject { ["fileUri"] = url,
                            ["mimeType"] = uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg" } });
                    else throw new ArgumentException("Vertex AI : image attendue en données base64 ou via HTTPS. / Expected a base64 or HTTPS image.");
                }
                else throw new ArgumentException("Vertex AI : type de contenu non pris en charge. / Unsupported content type.");
            }
        return result;
    }

    public static async IAsyncEnumerable<string> OpenAiLinesAsync(TextReader reader, string model,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var id = "chatcmpl-vertex-" + Guid.NewGuid().ToString("N");
        var rawParts = new JsonObject(); int nextPart = 0, nextCall = 0;
        bool finished = false;
        var data = new StringBuilder();
        IEnumerable<string> ConvertEvent(string value)
        {
            var chunk = JsonNode.Parse(value) ?? throw new JsonException("Empty Vertex response.");
            if (chunk["error"] != null) throw new IOException("Vertex AI : erreur signalée dans le flux. / Error in response stream.");
            var delta = new JsonObject(); string? finish = null;
            var candidate = (chunk["candidates"] as JsonArray)?.FirstOrDefault();
            if (candidate?["content"]?["parts"] is JsonArray parts)
            {
                var text = new StringBuilder(); var thoughts = new StringBuilder(); var calls = new JsonArray();
                foreach (var part in parts.OfType<JsonObject>())
                {
                    rawParts[(nextPart++).ToString("D8", System.Globalization.CultureInfo.InvariantCulture)] = part.DeepClone();
                    if (part["text"] is JsonNode fragment)
                    {
                        if (part["thought"]?.GetValue<bool>() == true) thoughts.Append(fragment.ToString());
                        else text.Append(fragment.ToString());
                    }
                    if (part["functionCall"] is JsonObject fn)
                    {
                        var call = new JsonObject { ["index"] = nextCall++, ["id"] = fn["id"]?.ToString() ?? "call_" + Guid.NewGuid().ToString("N"),
                            ["type"] = "function", ["function"] = new JsonObject { ["name"] = fn["name"]?.ToString() ?? "",
                                ["arguments"] = fn["args"]?.ToJsonString() ?? "{}" } };
                        if (part["thoughtSignature"] is JsonNode signature)
                            call["extra_content"] = new JsonObject { ["google"] = new JsonObject { ["thought_signature"] = signature.DeepClone() } };
                        calls.Add(call);
                    }
                    if (part["inlineData"] is JsonObject image)
                        text.Append("\n![Image](data:").Append(image["mimeType"]).Append(";base64,").Append(image["data"]).Append(")\n");
                }
                if (text.Length > 0) delta["content"] = text.ToString();
                if (thoughts.Length > 0) delta["reasoning_content"] = thoughts.ToString();
                if (calls.Count > 0) delta["tool_calls"] = calls;
                // Each event supplies only its new parts; ChatEngine recursively merges metadata without copying the entire history.
                var additions = new JsonObject();
                foreach (var part in rawParts) additions[part.Key] = part.Value?.DeepClone();
                rawParts.Clear();
                if (additions.Count > 0) delta["extra_content"] = new JsonObject { [Metadata] = new JsonObject { ["parts"] = additions } };
            }
            if (candidate?["finishReason"] is JsonNode reason)
            {
                finish = reason.ToString() switch { "STOP" => nextCall > 0 ? "tool_calls" : "stop", "MAX_TOKENS" => "length",
                    "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "IMAGE_SAFETY" => "content_filter", _ => reason.ToString().ToLowerInvariant() };
                finished = true;
            }
            if (chunk["promptFeedback"]?["blockReason"] is JsonNode blocked)
            { finish = "content_filter"; finished = true; delta["refusal"] = "Vertex AI: " + blocked.ToString(); }
            var result = new JsonObject { ["id"] = id, ["object"] = "chat.completion.chunk", ["model"] = model,
                ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }) };
            if (chunk["usageMetadata"] is JsonObject usage)
            {
                var output = (usage["candidatesTokenCount"]?.GetValue<int>() ?? 0) + (usage["thoughtsTokenCount"]?.GetValue<int>() ?? 0);
                result["usage"] = new JsonObject { ["prompt_tokens"] = usage["promptTokenCount"]?.DeepClone(), ["completion_tokens"] = output,
                    ["total_tokens"] = usage["totalTokenCount"]?.DeepClone(),
                    ["prompt_tokens_details"] = new JsonObject { ["cached_tokens"] = usage["cachedContentTokenCount"]?.DeepClone() } };
            }
            yield return "data: " + result.ToJsonString();
            yield return "";
        }
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0 && data.Length > 0)
            {
                foreach (var output in ConvertEvent(data.ToString().TrimEnd('\r', '\n'))) yield return output;
                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data.AppendLine(line[5..].TrimStart());
        }
        if (data.Length > 0) foreach (var output in ConvertEvent(data.ToString().TrimEnd('\r', '\n'))) yield return output;
        // Never invent a successful end when the upstream connection was interrupted.
        if (!finished) throw new IOException("Vertex AI : flux interrompu avant la fin, réponse partielle conservée. / Stream interrupted before completion.");
        yield return "data: [DONE]"; yield return "";
    }
}
