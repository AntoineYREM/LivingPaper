namespace LivingPaper.Evals;

// A score of the judge, attached to the root span of the try it grades. The id is stable (run, question, try): sending the same run
// again updates the scores instead of adding new ones.
internal sealed record LangfuseScore(
    string Id,
    string TraceId,
    string ObservationId,
    string Name,
    double Value,
    string? Comment,
    string Environment,
    string JudgePromptVersion);

internal static class LangfuseScores
{
    // Only what was graded and has a place in Langfuse: a failed try has no score, and a run made without Langfuse has no trace to
    // attach one to.
    public static IReadOnlyList<LangfuseScore> From(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return
        [
            .. run.Results
                .Where(result => result.Score.HasValue && result.TraceId is not null && result.SpanId is not null)
                .Select(result => new LangfuseScore(
                    $"{run.Id}-{result.QuestionId}-{result.Repeat}",
                    result.TraceId!,
                    result.SpanId!,
                    run.Judge.ScoreName,
                    result.Score!.Value,
                    string.IsNullOrWhiteSpace(result.Reason) ? null : result.Reason,
                    LangfuseNames.Environment,
                    run.Judge.PromptVersion)),
        ];
    }
}
