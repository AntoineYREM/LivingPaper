using System.Globalization;

namespace LivingPaper.Evals;

// What the replies of a run cost, and how often the search found a passage that holds the answer. Only the tries that have a trace
// count (a reply that failed has none). The tokens are averages for one reply; those of the query count the tries where none was
// written as zero. Searched is the number of tries where a passage was expected.
internal sealed record TraceSummary(
    int Tries,
    double ReplyInput,
    int MaxReplyInput,
    double ReplyOutput,
    double QueryInput,
    double QueryOutput,
    double MedianSeconds,
    int Searched,
    int Found,
    int RewriteFailed,
    int SearchFailed)
{
    // Null when no try has a trace (a run made by an earlier version of the tool).
    public static TraceSummary? Of(IEnumerable<EvalResult> results)
    {
        var traces = results.Select(result => result.Trace).OfType<TryTrace>().ToList();
        if (traces.Count == 0)
        {
            return null;
        }

        var seconds = traces.Select(trace => trace.Seconds).Order().ToList();
        return new TraceSummary(
            traces.Count,
            traces.Average(trace => trace.ReplyTokens.Input),
            traces.Max(trace => trace.ReplyTokens.Input),
            traces.Average(trace => trace.ReplyTokens.Output),
            traces.Average(trace => trace.QueryTokens?.Input ?? 0),
            traces.Average(trace => trace.QueryTokens?.Output ?? 0),
            (seconds[(seconds.Count - 1) / 2] + seconds[seconds.Count / 2]) / 2,
            traces.Count(trace => trace.ExpectedFound.HasValue),
            traces.Count(trace => trace.ExpectedFound == true),
            traces.Count(trace => trace.Notices?.Contains("RewriteFailed", StringComparer.Ordinal) == true),
            traces.Count(trace => trace.Notices?.Contains("SearchFailed", StringComparer.Ordinal) == true));
    }

    public void Write(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}prompt of the reply: {Whole(ReplyInput)} tokens on average, {MaxReplyInput} at most; reply: {Whole(ReplyOutput)} tokens");
        if (QueryInput > 0 || QueryOutput > 0)
        {
            writer.WriteLine($"{indent}writing of the search query: {Whole(QueryInput)} tokens in, {Whole(QueryOutput)} out");
        }

        writer.WriteLine($"{indent}time of a reply: {MedianSeconds.ToString("F1", CultureInfo.InvariantCulture)} s (median)");
        if (Searched > 0)
        {
            writer.WriteLine($"{indent}expected passage found: {Found} of {Searched} ({Whole(100.0 * Found / Searched)}%)");
        }

        if (RewriteFailed > 0 || SearchFailed > 0)
        {
            writer.WriteLine($"{indent}fallbacks: query not written {RewriteFailed}, search failed {SearchFailed}");
        }
    }

    private static string Whole(double value) => value.ToString("F0", CultureInfo.InvariantCulture);
}
