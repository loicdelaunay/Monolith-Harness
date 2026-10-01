using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public static class LocalModelImport
{
    public static async Task<LocalModel> ImportAsync(string source, string purpose, string family, LocalProviderSettings settings,
        bool copy, IProgress<LocalTransferProgress>? progress, CancellationToken ct)
    {
        source = Path.GetFullPath(source); SandboxWorkspace.AssertNoLinks(source);
        var detectedFamily = await InspectAsync(source, purpose, ct);
        var context = purpose == "chat" ? await Task.Run(() => GgufContext.Read(source, ct), ct) : null;
        var destination = source;
        if (copy)
        {
            Directory.CreateDirectory(settings.ModelDirectory); SandboxWorkspace.AssertNoLinks(settings.ModelDirectory);
            destination = Path.Combine(settings.ModelDirectory, Path.GetFileName(source));
            if (!PlatformSupport.PathComparer.Equals(destination, source))
            {
                if (File.Exists(destination)) throw new IOException("Un modèle du même nom existe déjà dans le dossier model.");
                var partial = destination + ".partial-" + Guid.NewGuid().ToString("N");
                try
                {
                    await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                    await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                    {
                        var buffer = new byte[128 * 1024]; long copied = 0; var last = Environment.TickCount64;
                        while (true) { var count = await input.ReadAsync(buffer, ct); if (count == 0) break; await output.WriteAsync(buffer.AsMemory(0, count), ct); copied += count; if (Environment.TickCount64 - last > 150) { progress?.Report(new(copied, input.Length, "Import")); last = Environment.TickCount64; } }
                    }
                    ct.ThrowIfCancellationRequested(); File.Move(partial, destination, false);
                }
                finally { if (File.Exists(partial)) File.Delete(partial); }
            }
        }
        return new() { Id = Path.GetFileNameWithoutExtension(destination), Path = LocalProviderSettings.StoredPath(destination), Purpose = purpose,
            Family = purpose == "image" ? detectedFamily : family, Bytes = new FileInfo(destination).Length, ContextTokens = context };
    }
    public static async Task<string> InspectAsync(string path, string purpose, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var prefix = new byte[8]; await file.ReadExactlyAsync(prefix, ct);
        if (path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            if (!prefix.AsSpan(0, 4).SequenceEqual("GGUF"u8) || BitConverter.ToUInt32(prefix, 4) is not (2 or 3)) throw new IOException("Ce fichier n’est pas un modèle GGUF valide.");
            if (purpose == "image") throw new IOException("Les GGUF de diffusion demandent généralement des encodeurs et un VAE séparés. Importez un checkpoint complet Safetensors SD/SDXL pour cette bêta.");
            return "";
        }
        if (purpose != "image" || !path.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)) throw new IOException("Format non supporté : GGUF pour le chat, checkpoint Safetensors complet pour les images.");
        var length = BitConverter.ToUInt64(prefix);
        if (length is < 2 or > 32 * 1024 * 1024 || (long)length > file.Length - 8) throw new IOException("En-tête Safetensors invalide.");
        var header = new byte[(int)length]; await file.ReadExactlyAsync(header, ct);
        var tensors = await Task.Run(() => JsonNode.Parse(header)?.AsObject() ?? throw new IOException("En-tête Safetensors invalide."), ct);
        var names = tensors.Select(x => x.Key).ToList();
        if (!names.Any(x => x.StartsWith("model.diffusion_model.input_blocks.")) || !names.Any(x => x.StartsWith("first_stage_model.")) ||
            !names.Any(x => x.StartsWith("cond_stage_model.") || x.StartsWith("conditioner.embedders.")))
            throw new IOException("Checkpoint incomplet : diffusion, encodeur de texte et VAE doivent être inclus. Les composants Diffusers et les LoRA ne sont pas des modèles autonomes.");
        return names.Any(x => x.StartsWith("conditioner.embedders.")) ? "sdxl" : names.Any(x => x.StartsWith("cond_stage_model.model.")) ? "sd2" : "sd1";
    }
}
