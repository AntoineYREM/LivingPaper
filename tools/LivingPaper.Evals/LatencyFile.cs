using System.Text.Json;

namespace LivingPaper.Evals;

// One line of evals/published/latency.json, as the charts need it.
internal sealed record LatencyEntry(string RunId, string Model, string Dataset, double MeanSeconds, double? FirstTextSeconds);

// Reads the file that the command `latency` wrote (with --out). Checked like any file: its schema and its size.
internal static class LatencyFile
{
    public const long MaxFileBytes = 1_048_576;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<LatencyEntry>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new EvalException($"The times file {name} does not exist: make it with the command latency and --out.");
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new EvalException($"The times file {name} is too large.");
        }

        Content? content;
        try
        {
            await using var stream = File.OpenRead(path);
            content = await JsonSerializer.DeserializeAsync<Content>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new EvalException($"The times file {name} is not valid JSON.");
        }

        if (content is null || content.SchemaVersion != 1 || content.Runs is null)
        {
            throw new EvalException($"The times file {name} must have \"schemaVersion\": 1 and a list of runs.");
        }

        return
        [
            .. content.Runs
                .Where(run => !string.IsNullOrWhiteSpace(run.RunId) && !string.IsNullOrWhiteSpace(run.Model) && !string.IsNullOrWhiteSpace(run.Dataset))
                .Select(run => new LatencyEntry(run.RunId!, run.Model!, run.Dataset!, run.MeanSeconds, run.MeanFirstTextSeconds)),
        ];
    }

    private sealed record Content(int SchemaVersion, List<Line>? Runs);

    private sealed record Line(string? RunId, string? Model, string? Dataset, double MeanSeconds, double? MeanFirstTextSeconds);
}
