using System.Globalization;

namespace LivingPaper.Evals;

internal sealed record CategorySummary(string Category, int Scored, int Failed, double Average, int Min);

internal sealed record RunSummary(IReadOnlyList<CategorySummary> Categories, double? Average, int Scored, int Failed);

// The numbers of a run, and the page that shows them. Only what was graded is averaged: a failure is counted and listed, never
// turned into a score.
internal static class ReportWriter
{
    // A reply graded this low or lower is listed with the reason of the judge.
    public const int LowScore = 2;

    public static RunSummary Summarize(IEnumerable<EvalResult> results, IReadOnlyList<string> categoryOrder)
    {
        var all = results.ToList();
        var categories = categoryOrder.Concat(all.Select(result => result.Category)).Distinct(StringComparer.Ordinal).ToList();
        var summaries = new List<CategorySummary>();
        foreach (var category in categories)
        {
            var ofCategory = all.Where(result => result.Category == category).ToList();
            if (ofCategory.Count == 0)
            {
                continue;
            }

            var scores = ofCategory.Where(result => result.Score.HasValue).Select(result => result.Score!.Value).ToList();
            summaries.Add(new CategorySummary(
                category,
                scores.Count,
                ofCategory.Count - scores.Count,
                scores.Count == 0 ? 0 : scores.Average(),
                scores.Count == 0 ? 0 : scores.Min()));
        }

        var allScores = all.Where(result => result.Score.HasValue).Select(result => result.Score!.Value).ToList();
        return new RunSummary(summaries, allScores.Count == 0 ? null : allScores.Average(), allScores.Count, all.Count - allScores.Count);
    }

    public static void Write(TextWriter writer, RunSummary summary, IReadOnlyList<EvalResult> results, TokenUsage chat, TokenUsage judge)
    {
        var width = Math.Max("Category".Length, summary.Categories.Select(category => category.Category.Length).DefaultIfEmpty(0).Max());
        writer.WriteLine();
        writer.WriteLine($"{"Category".PadRight(width)}  Graded  Failed  Average  Lowest");
        foreach (var category in summary.Categories)
        {
            writer.WriteLine(
                $"{category.Category.PadRight(width)}  {category.Scored,6}  {category.Failed,6}  {Format(category.Scored == 0 ? null : category.Average),7}  {(category.Scored == 0 ? "-" : category.Min.ToString(CultureInfo.InvariantCulture)),6}");
        }

        writer.WriteLine();
        writer.WriteLine($"Average: {Format(summary.Average)} ({summary.Scored} graded, {summary.Failed} failed)");

        var low = results.Where(result => result.Score <= LowScore).ToList();
        if (low.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"Lowest scores ({LowScore} or less), with the reason of the judge:");
            foreach (var result in low)
            {
                writer.WriteLine($"  {result.QuestionId} (try {result.Repeat}): {result.Score} - {result.Reason}");
            }
        }

        var failed = results.Where(result => result.Failure is not null).ToList();
        if (failed.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Not graded:");
            foreach (var result in failed)
            {
                writer.WriteLine($"  {result.QuestionId} (try {result.Repeat}): {result.Failure}");
            }
        }

        writer.WriteLine();
        writer.WriteLine($"Tokens, chat:  {Describe(chat)}");
        writer.WriteLine($"Tokens, judge: {Describe(judge)}");
    }

    private static string Format(double? average) => average is null ? "-" : average.Value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Describe(TokenUsage usage) => $"{usage.Input} in ({usage.CachedInput} cached), {usage.Output} out";
}
