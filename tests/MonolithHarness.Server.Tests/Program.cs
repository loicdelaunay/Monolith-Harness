// Integration checks of MonolithHarness.Server. Each area starts its own in-process server on a free port.
// Usage: dotnet run --project tests/MonolithHarness.Server.Tests [-- --only accounts,providers]
using System.Text.Json;

int passed = 0, failed = 0;
var failures = new List<string>();
void Check(bool condition, string name)
{
    if (condition) { Console.WriteLine("OK : " + name); passed++; }
    else { Console.WriteLine("ÉCHEC : " + name); failed++; failures.Add(name); }
}

var only = args.SkipWhile(x => x != "--only").Skip(1).FirstOrDefault()?.Split(',') ?? [];
async Task Area(string name, Func<Action<bool, string>, Task> run)
{
    if (only.Length > 0 && !only.Contains(name)) return;
    Console.WriteLine($"--- {name}");
    try { await run(Check); }
    catch (Exception ex) { Check(false, $"{name}: exception inattendue — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
}

await Area("accounts", AccountChecks.Run);
await Area("admin", AdminChecks.Run);
await Area("providers", ProviderChecks.Run);
await Area("projects", ProjectChecks.Run);
await Area("queue", QueueChecks.Run);
await Area("schedules", ScheduleChecks.Run);
await Area("shares", ShareChecks.Run);
await Area("realtime", RealtimeChecks.Run);
await Area("worker", WorkerChecks.Run);
await Area("clients", ClientChecks.Run);
await Area("probe", ProviderProbeChecks.Run);
await Area("relayqueue", RelayQueueChecks.Run);
await Area("sharecodes", ShareCodeChecks.Run);
await Area("sync", SyncChecks.Run);
await Area("liveview", LiveViewChecks.Run);

Console.WriteLine();
Console.WriteLine($"{passed} contrôles réussis, {failed} échec(s).");
foreach (var failure in failures) Console.WriteLine("  - " + failure.Split('\n')[0]);
return failed == 0 ? 0 : 1;
