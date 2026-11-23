namespace LivingPaper.Evals;

// The time the model of the chat took to answer in one run: the mean, the median and the slowest tenth, in seconds, and the mean time until
// the first text. Answers is the number of answers of the run; Calls the number of timed calls found (a retried answer counts more than once).
internal sealed record RunLatency(
    string RunId,
    string? Label,
    string Model,
    string Dataset,
    int Answers,
    int Calls,
    double MeanSeconds,
    double MedianSeconds,
    double P90Seconds,
    double? MeanFirstTokenSeconds,
    int FirstTokenCalls)
{
    // The calls of a run are those of its model that started while it ran. Null when there are none: the run was not sent to Langfuse, or
    // the keys are of another project.
    public static RunLatency? Of(RunRecord run, IReadOnlyList<ChatTiming> timings)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(timings);

        var calls = timings
            .Where(timing => timing.Model == run.Chat.Model && timing.Start >= run.StartedAt && timing.Start <= run.FinishedAt)
            .ToList();
        if (calls.Count == 0)
        {
            return null;
        }

        var seconds = calls.Select(call => call.LatencySeconds).Order().ToList();
        var firstTokens = calls.Where(call => call.FirstTokenSeconds.HasValue).Select(call => call.FirstTokenSeconds!.Value).ToList();
        return new RunLatency(
            run.Id,
            run.Label,
            run.Chat.Model,
            run.Dataset,
            run.Results.Count,
            calls.Count,
            seconds.Average(),
            Median(seconds),
            seconds[(int)Math.Floor(0.9 * (seconds.Count - 1))],
            firstTokens.Count == 0 ? null : firstTokens.Average(),
            firstTokens.Count);
    }

    private static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
}
