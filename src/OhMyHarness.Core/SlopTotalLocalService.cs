using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OhMyHarness.Core;

/// <summary>Optional, user-started Docker service; never installs Docker or changes another container.</summary>
public static class SlopTotalLocalService
{
    const string Revision = "8d7fa6ff58a9a2a553d890ecb2cd00a9d16295f1";
    const string Container = "monolith-harness-sloptotal", Label = "monolith-harness.ai-detector", Image = "monolith-harness-sloptotal:" + Revision;
    static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task StartAsync(IProgress<string>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await RunAsync(["info", "--format", "{{.ServerVersion}}"], null, null, ct);
            var exists = await RunAsync(["container", "ls", "-a", "--filter", "name=^/" + Container + "$", "--format", "{{.Names}}"], null, null, ct);
            if (exists.Trim() == Container)
            {
                await AssertOwnedAsync(ct); await RunAsync(["start", Container], null, progress, ct); return;
            }
            // CPU-only PyTorch avoids downloading CUDA libraries. Upstream sources retain their MIT license.
            var dockerfile = $$"""
                FROM python:3.11-slim
                WORKDIR /app
                ADD https://codeload.github.com/pablocaeg/sloptotal/tar.gz/{{Revision}} /tmp/sloptotal.tar.gz
                RUN tar -xzf /tmp/sloptotal.tar.gz -C /app --strip-components=1 && rm /tmp/sloptotal.tar.gz
                RUN python -m pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu
                RUN python -m pip install --no-cache-dir -r requirements.txt "transformers<5" psutil pypdf python-docx peft sentencepiece
                RUN useradd -r -s /bin/false sloptotal && mkdir -p /app/models /app/data && chown -R sloptotal:sloptotal /app/models /app/data
                USER sloptotal
                ENV HF_HOME=/app/models
                EXPOSE 8000
                CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
                """;
            await RunAsync(["build", "--tag", Image, "-"], dockerfile, progress, ct);
            await RunAsync(["run", "--detach", "--name", Container, "--label", Label + "=true", "--publish", "127.0.0.1:8786:8000",
                "--mount", "type=volume,source=monolith-harness-sloptotal-models,target=/app/models", "--mount", "type=volume,source=monolith-harness-sloptotal-data,target=/app/data", Image], null, progress, ct);
        }
        catch (Win32Exception ex) { throw new IOException("Docker est introuvable. Installez et démarrez Docker, ou connectez un serveur SlopTotal existant. / Docker was not found. Install and start Docker, or connect an existing SlopTotal server.", ex); }
        finally { Gate.Release(); }
    }
    public static async Task StopAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try { await AssertOwnedAsync(ct); await RunAsync(["stop", Container], null, null, ct); }
        finally { Gate.Release(); }
    }
    static async Task AssertOwnedAsync(CancellationToken ct)
    {
        var label = await RunAsync(["inspect", "--format", "{{index .Config.Labels \"" + Label + "\"}}", Container], null, null, ct);
        if (label.Trim() != "true") throw new IOException("Ce conteneur n’appartient pas à Monolith Harness. / This container does not belong to Monolith Harness.");
    }
    static async Task<string> RunAsync(string[] args, string? input, IProgress<string>? progress, CancellationToken ct)
    {
        var start = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Docker.");
        using var registration = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = new StringBuilder(); var error = new StringBuilder();
        async Task Read(StreamReader reader, StringBuilder target)
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                target.AppendLine(line); if (target.Length > 6000) target.Remove(0, target.Length - 6000);
                progress?.Report(line.Length > 220 ? line[^220..] : line);
            }
        }
        var stdout = Read(process.StandardOutput, output); var stderr = Read(process.StandardError, error);
        if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), ct); process.StandardInput.Close();
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(ct)); ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException("Docker · " + error.ToString().Trim());
        return output.ToString();
    }
}
