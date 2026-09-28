using OhMyHarness.Core;

static class OpenCodeBypassChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var provider = new Provider
        {
            Name = "OpenCode Test",
            Kind = "opencode",
            BaseUrl = "http://127.0.0.1:4096",
            BypassFreeLimitation = true
        };
        check(provider.BypassFreeLimitation, "Provider.BypassFreeLimitation persisté et accessible");
        check(OpenCodeEngine.IsFreeModel("opencode/big-pickle"), "IsFreeModel détecte big-pickle");
        check(OpenCodeEngine.IsFreeModel("meta/llama-3-8b-instruct:free"), "IsFreeModel détecte :free");
        check(OpenCodeEngine.IsFreeModel("zen-free-mini"), "IsFreeModel détecte -free");
        check(OpenCodeEngine.IsFreeModel("zen"), "IsFreeModel détecte zen");
        check(!OpenCodeEngine.IsFreeModel("openai/gpt-4o"), "IsFreeModel rejette les modèles payants");

        var req = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:4096/models");
        OpenCodeEngine.Configure(req, provider, "password123");
        check(req.Headers.Contains("x-opencode-client") && req.Headers.GetValues("x-opencode-client").First() == "desktop",
            "Configure ajoute x-opencode-client: desktop lorsque BypassFreeLimitation est activé");
        check(req.Headers.Contains("x-bypass-free-limitation"),
            "Configure ajoute x-bypass-free-limitation lorsque BypassFreeLimitation est activé");

        provider.BypassFreeLimitation = false;
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:4096/models");
        OpenCodeEngine.Configure(req2, provider, "password123");
        check(!req2.Headers.Contains("x-bypass-free-limitation"),
            "Configure n'ajoute pas x-bypass-free-limitation lorsque l'option est désactivée");
        await Task.CompletedTask;
    }
}
