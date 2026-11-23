using LivingPaper.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LivingPaper.Evals;

// The `latency` command: reads the runs, asks Langfuse how long the model of the chat took for each of them (it reads, it writes nothing there),
// prints the figures and, with --out, keeps them in a JSON file.
internal static class LatencyCommand
{
    // The calls are looked for from a little before the first run started, to forgive a clock that is not quite the same.
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    public static async Task<int> RunAsync(LatencyOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var evalsFolder = RepoFiles.Find("evals") ?? Path.Combine(Directory.GetCurrentDirectory(), "evals");
        string[] folders = [Path.Combine(evalsFolder, "runs"), Path.Combine(evalsFolder, "published")];

        var runs = new List<RunRecord>();
        foreach (var name in options.Runs)
        {
            runs.Add(await RunStore.LoadAsync(RunLocator.Resolve(name, folders), cancellationToken).ConfigureAwait(false));
        }

        LangfuseOptions langfuse;
        try
        {
            langfuse = LangfuseSettings.Read(new ConfigurationBuilder().AddEnvironmentVariables().Build())
                ?? throw new EvalException("latency reads Langfuse: set LANGFUSE_BASE_URL, LANGFUSE_PUBLIC_KEY and LANGFUSE_SECRET_KEY (shell or .env).");
        }
        catch (InvalidOperationException exception)
        {
            throw new EvalException(exception.Message);
        }

        using var api = new LangfuseApi(langfuse);
        var timings = await api.GetChatTimingsAsync(runs.Min(run => run.StartedAt) - Margin, cancellationToken).ConfigureAwait(false);

        var latencies = new List<RunLatency>();
        var withoutData = new List<string>();
        foreach (var run in runs)
        {
            if (RunLatency.Of(run, timings) is { } latency)
            {
                latencies.Add(latency);
            }
            else
            {
                withoutData.Add(run.Id);
            }
        }

        LatencyWriter.Write(output, latencies, withoutData);
        if (options.OutPath is not null && latencies.Count > 0)
        {
            await File.WriteAllTextAsync(options.OutPath, LatencyWriter.ToJson(latencies, DateTimeOffset.UtcNow) + "\n", cancellationToken).ConfigureAwait(false);
            output.WriteLine();
            output.WriteLine($"Written: {options.OutPath}");
        }

        return latencies.Count == 0 ? 1 : 0;
    }
}
