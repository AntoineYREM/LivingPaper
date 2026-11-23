using System.Globalization;
using System.Text.Json;

namespace LivingPaper.Evals;

// The page of `latency` and its JSON file. The figures are in seconds, with two decimals.
internal static class LatencyWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(TextWriter writer, IReadOnlyList<RunLatency> latencies, IReadOnlyList<string> withoutData)
    {
        writer.WriteLine("Time to answer, from the calls recorded in Langfuse (seconds, the judge is not counted):");
        writer.WriteLine();
        var width = Math.Max("Model".Length, latencies.Select(latency => latency.Model.Length).DefaultIfEmpty(0).Max());
        writer.WriteLine($"{"Model".PadRight(width)}  {"Label",-14}  Answers  Calls    Mean  Median     P90  First text");
        foreach (var latency in latencies)
        {
            writer.WriteLine(
                $"{latency.Model.PadRight(width)}  {(latency.Label ?? "-"),-14}  {latency.Answers,7}  {latency.Calls,5}  {Seconds(latency.MeanSeconds),6}  {Seconds(latency.MedianSeconds),6}  {Seconds(latency.P90Seconds),6}  {(latency.MeanFirstTokenSeconds is { } first ? Seconds(first) : "-"),10}");
        }

        foreach (var id in withoutData)
        {
            writer.WriteLine();
            writer.WriteLine($"No timing found in Langfuse for {id}: it was not sent there, or this is another project.");
        }
    }

    // The same figures for a file kept in the repository. It holds no key, no address and no text: the id of each run, its model and numbers.
    public static string ToJson(IReadOnlyList<RunLatency> latencies, DateTimeOffset measuredAt) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                measuredAt = measuredAt.UtcDateTime,
                source = "Langfuse: the calls named chat-reply, in the environment experiment. Seconds. The judge is not counted.",
                runs = latencies.Select(latency => new
                {
                    runId = latency.RunId,
                    label = latency.Label,
                    model = latency.Model,
                    dataset = latency.Dataset,
                    answers = latency.Answers,
                    calls = latency.Calls,
                    meanSeconds = Round(latency.MeanSeconds),
                    medianSeconds = Round(latency.MedianSeconds),
                    p90Seconds = Round(latency.P90Seconds),
                    meanFirstTextSeconds = latency.MeanFirstTokenSeconds is { } first ? Round(first) : (double?)null,
                    callsWithFirstText = latency.FirstTokenCalls,
                }),
            },
            JsonOptions);

    private static double Round(double value) => Math.Round(value, 2);

    private static string Seconds(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
