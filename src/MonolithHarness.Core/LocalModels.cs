using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed class LocalModel
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Purpose { get; set; } = "chat";
    public string Family { get; set; } = "";
    public string Repository { get; set; } = "";
    public long Bytes { get; set; }
    public int? ContextTokens { get; set; }
    public override string ToString() => Id;
    public string FullPath => System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(Path) ? Path : System.IO.Path.Combine(PortableStorage.Root, Path));
}

public sealed class LocalProviderSettings
{
    public string Directory { get; set; } = "";
    public string Backend { get; set; } = "auto";
    public string ChatExecutable { get; set; } = "";
    public string ImageExecutable { get; set; } = "";
    public List<LocalModel> Models { get; set; } = [];
    public string ModelDirectory => string.IsNullOrWhiteSpace(Directory) ? System.IO.Path.Combine(PortableStorage.Root, "model") : System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(Directory) ? Directory : System.IO.Path.Combine(PortableStorage.Root, Directory));
    public static LocalProviderSettings Read(string json)
    {
        try { return JsonSerializer.Deserialize<LocalProviderSettings>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
    public string Json() => JsonSerializer.Serialize(this);
    public LocalModel Find(string id, string purpose) => Models.FirstOrDefault(x => x.Id == id && x.Purpose == purpose)
        ?? throw new InvalidOperationException("Modèle local introuvable. Importez-le dans Fournisseurs → Local. / Import this model in Providers → Local.");
    public void Add(LocalModel model)
    {
        Models.RemoveAll(x => PlatformSupport.PathComparer.Equals(x.FullPath, model.FullPath));
        if (Models.Any(x => x.Id == model.Id)) model.Id += " · " + Guid.NewGuid().ToString("N")[..6];
        Models.Add(model);
    }
    public static string StoredPath(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var relative = System.IO.Path.GetRelativePath(PortableStorage.Root, path);
        return relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar) || System.IO.Path.IsPathRooted(relative) ? path : relative;
    }
}

public sealed record LocalCompatibility(string Status, string Reason, long RequiredMemory, bool CanLoad)
{
    public string Label => Status switch { "supported" => "Supporté (estimé)", "slow" => "Supporté · CPU / partiel", "unknown" => "À vérifier", _ => "Non supporté" };
}

public static class LocalModelCompatibility
{
    const long GiB = 1024L * 1024 * 1024;
    public static LocalCompatibility Estimate(string filename, string purpose, string family, long bytes, MachineCapabilities machine, int context = 8192, int width = 1024, int height = 1024, string backend = "auto")
    {
        if (!machine.RuntimeSupported) return new("unsupported", "Cette intégration nécessite Windows/Linux x64 avec AVX2, ou macOS Apple Silicon. Le système, l’architecture ou les instructions du CPU ne conviennent pas au moteur précompilé.", 0, false);
        var lower = filename.ToLowerInvariant();
        if (lower.Contains("mmproj") || lower.Contains("lora") || lower.Contains("adapter") || lower.Contains("-of-"))
            return new("unsupported", "Projecteur, adaptateur ou fichier fractionné : ce fichier ne constitue pas un modèle autonome.", 0, false);
        if (purpose == "chat" && !lower.EndsWith(".gguf"))
            return new("unsupported", "Le moteur de conversation charge des modèles GGUF autonomes.", 0, false);
        if (purpose == "image" && family is not ("sd1" or "sd2" or "sdxl"))
            return new("unsupported", "Cette bêta charge les checkpoints complets SD 1.x, SD 2.x et SDXL. Flux, SD3, les encodeurs/VAE séparés et les dossiers Diffusers demandent une autre configuration.", 0, false);
        if (purpose == "image" && (lower.EndsWith(".gguf") || lower.Contains("diffusion_pytorch_model") || lower.Split('/').Any(x => x is "unet" or "vae" or "transformer" or "text_encoder" or "text_encoder_2")))
            return new("unsupported", "Composant de diffusion isolé : encodeurs de texte et VAE manquants. Cette bêta attend un checkpoint complet Safetensors.", 0, false);
        if (purpose == "image" && !lower.EndsWith(".safetensors") && !lower.EndsWith(".gguf"))
            return new("unsupported", "Checkpoint Safetensors ou GGUF requis ; les fichiers Pickle/CKPT ne sont pas importés.", 0, false);
        if (bytes <= 0) return new("unknown", "Taille inconnue : impossible d’estimer la mémoire nécessaire avant l’import.", 0, true);
        var overhead = purpose == "image" ? (long)((family == "sdxl" ? 5 * GiB : 2 * GiB) * Math.Max(1, (long)width * height / 1048576d)) : (long)(Math.Clamp(context, 1024, 262144) / 8192d * GiB);
        var required = (long)(bytes * (purpose == "image" ? 1.25 : 1.15)) + overhead;
        if (backend != "cpu" && machine.VramBytes > required && !machine.VramEstimated && machine.RamBytes >= 4 * GiB)
            return new("supported", $"Format accepté ; environ {MachineCapabilities.Size(required)} de mémoire à prévoir, VRAM détectée : {MachineCapabilities.Size(machine.VramBytes)}. Offload GPU envisageable, sous réserve de VRAM libre et de pilotes compatibles. Architecture confirmée au chargement.", required, true);
        if (machine.RamBytes > 0 && required > machine.RamBytes * .85)
            return new("unsupported", $"Environ {MachineCapabilities.Size(required)} nécessaires, au-delà de la RAM utilisable ({MachineCapabilities.Size(machine.RamBytes)} installés). Choisissez une quantification ou un contexte plus petit.", required, false);
        var free = machine.AvailableRamBytes;
        if (free > 0 && required > free)
            return new("unknown", $"Environ {MachineCapabilities.Size(required)} nécessaires ; seulement {MachineCapabilities.Size(free)} de RAM libre actuellement. Fermez des applications avant de charger.", required, true);
        if (machine.RamBytes <= 0) return new("unknown", "RAM non détectée. Le format est accepté, mais la capacité de chargement reste inconnue.", required, true);
        return new("slow", $"Format accepté ; environ {MachineCapabilities.Size(required)} de RAM à prévoir. Une partie ou tout le calcul peut rester sur CPU : débit non garanti. Le contexte et la résolution changent cette estimation.", required, true);
    }

    public static string ImageFamily(string repository, JsonObject? info = null)
    {
        var value = (repository + " " + info?["cardData"]?["base_model"]?.ToJsonString()).ToLowerInvariant();
        if (value.Contains("flux") || value.Contains("sd3") || value.Contains("stable-diffusion-3")) return "";
        if (value.Contains("sdxl") || value.Contains("stable-diffusion-xl")) return "sdxl";
        if (value.Contains("stable-diffusion-2") || value.Contains("sd2")) return "sd2";
        if (value.Contains("stable-diffusion-v1") || value.Contains("stable-diffusion-1") || value.Contains("sd1") || value.Contains("v1-5")) return "sd1";
        return "";
    }
}
