using System.Text;
using Sweepstake.StatsFetcher;

// Player names carry accents, and a Windows console defaults to a codepage that mangles them.
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Output is redirected somewhere that will not take an encoding change. Not fatal.
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;

try
{
    var paths = RepoPaths.Discover();

    switch (command)
    {
        case "verify":
            return await VerifyCommand.RunAsync(paths, cts.Token);

        case "fetch":
            return await FetchCommand.RunAsync(paths, rebuildEverything: args.Contains("--rebuild", StringComparer.OrdinalIgnoreCase), cts.Token);

        default:
            return Usage(command);
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Usage(string command)
{
    if (!string.IsNullOrEmpty(command))
    {
        Console.Error.WriteLine($"error: unknown command \"{command}\".");
        Console.Error.WriteLine();
    }

    Console.Error.WriteLine("usage: dotnet run --project tools/Sweepstake.StatsFetcher -- <command>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  fetch    Refresh wwwroot/data/stats.json by summing live per-fixture results.");
    Console.Error.WriteLine("           Run by CI. Exits non-zero without writing if the fetch fails.");
    Console.Error.WriteLine("           --rebuild  re-read every fixture, ignoring the settled-fixture cache.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  verify   Confirm the athlete ids in data/picks.json still resolve to the");
    Console.Error.WriteLine("           expected players. Maintenance only - run after a transfer window,");
    Console.Error.WriteLine("           never in the deploy path. Never edits picks.json.");
    return 64; // EX_USAGE
}
