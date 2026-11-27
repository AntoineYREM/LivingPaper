using System.Globalization;

namespace LivingPaper.Evals;

// The page of `compare`: who is compared, what may spoil the comparison, the averages by category with their difference, the questions that
// differ most, and what each run cost. A is the baseline and B the candidate: a positive difference means B did better.
internal static class ComparisonWriter
{
    private const string Signed = "+0.00;-0.00;0.00";
    private const string SignedOneDecimal = "+0.0;-0.0;0.0";
    private const int BiggestShown = 5;

    public static void Write(TextWriter writer, RunComparison comparison, PriceList? prices)
    {
        Describe(writer, "A", comparison.A);
        Describe(writer, "B", comparison.B);

        foreach (var warning in comparison.Warnings)
        {
            writer.WriteLine();
            writer.WriteLine($"Warning: {warning}");
        }

        WriteCategories(writer, comparison);
        WriteGrades(writer, comparison);
        WriteBiggest(writer, comparison);
        WriteCost(writer, comparison, prices);
        WriteReplies(writer, comparison);
    }

    private static void WriteReplies(TextWriter writer, RunComparison comparison)
    {
        foreach (var (label, run) in new[] { ("A", comparison.A), ("B", comparison.B) })
        {
            if (TraceSummary.Of(run.Results) is { } traces)
            {
                writer.WriteLine();
                writer.WriteLine($"Per reply, {label} ({Reading(run)}):");
                traces.Write(writer, "  ");
            }
        }
    }

    // How the run gave the book to the character.
    private static string Reading(RunRecord run) => run.Retrieval is { } retrieval
        ? $"{retrieval.Passages} passages ({retrieval.EmbeddingModel}), query mode {retrieval.QueryMode}"
        : "the whole story";

    private static void Describe(TextWriter writer, string label, RunRecord run)
    {
        writer.WriteLine($"{label}: {run.Id}");
        writer.WriteLine($"   {run.Character} answers with {run.Chat.Provider} ({run.Chat.Model}); {run.Judge.Provider} ({run.Judge.Model}) judges, prompt version {run.Judge.PromptVersion}; {run.Repeats} time(s) each.");
        if (run.Label is not null || run.PromptsHash is not null)
        {
            writer.WriteLine($"   {(run.Label is null ? string.Empty : $"label \"{run.Label}\"; ")}chat prompts {run.PromptsHash ?? "unknown"}.");
        }
    }

    private static void WriteCategories(TextWriter writer, RunComparison comparison)
    {
        var width = Math.Max("Category".Length, comparison.Categories.Select(category => category.Category.Length).DefaultIfEmpty(0).Max());
        writer.WriteLine();
        writer.WriteLine($"{"Category".PadRight(width)}  Questions       A       B   B - A");
        foreach (var category in comparison.Categories)
        {
            writer.WriteLine(
                $"{category.Category.PadRight(width)}  {category.Questions,9}  {Format(category.AverageA),6}  {Format(category.AverageB),6}  {category.Delta.ToString(Signed, CultureInfo.InvariantCulture),6}");
        }

        writer.WriteLine();
        if (comparison.Overall is not { } overall)
        {
            writer.WriteLine("Overall: fewer than two questions were graded in both runs, so there is no interval to give.");
            return;
        }

        var meanA = comparison.Questions.Average(question => question.MeanA);
        var meanB = comparison.Questions.Average(question => question.MeanB);
        writer.WriteLine(
            $"Overall over {overall.N} questions: A {Format(meanA)}, B {Format(meanB)}, B - A {overall.Mean.ToString(Signed, CultureInfo.InvariantCulture)} " +
            $"(95% interval {overall.Low.ToString(Signed, CultureInfo.InvariantCulture)} to {overall.High.ToString(Signed, CultureInfo.InvariantCulture)}).");
        writer.WriteLine(overall.ExcludesZero
            ? $"The interval does not include 0: B really looks {(overall.Mean > 0 ? "better" : "worse")} than A on these questions (the judge and the questions chosen still count)."
            : "The interval includes 0: the difference could be noise. More questions or more tries would be needed to tell.");
    }

    // The tries of the questions both runs graded, so that both counts are about the same questions.
    private static void WriteGrades(TextWriter writer, RunComparison comparison)
    {
        var compared = comparison.Questions.Select(question => question.QuestionId).ToHashSet(StringComparer.Ordinal);
        writer.WriteLine();
        writer.WriteLine("Replies by grade:");
        foreach (var (label, run) in new[] { ("A", comparison.A), ("B", comparison.B) })
        {
            writer.WriteLine($"  {label}: {GradeCounts.Describe(run.Results.Where(result => compared.Contains(result.QuestionId)))}");
        }
    }

    private static void WriteBiggest(TextWriter writer, RunComparison comparison)
    {
        var biggest = RunComparison.Biggest(comparison.Questions, BiggestShown).Where(question => question.Delta != 0).ToList();
        if (biggest.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("Questions that differ most (mean of the tries):");
        foreach (var question in biggest)
        {
            writer.WriteLine(
                $"  {question.QuestionId} ({question.Category}): A {question.MeanA.ToString("F1", CultureInfo.InvariantCulture)}, B {question.MeanB.ToString("F1", CultureInfo.InvariantCulture)} ({question.Delta.ToString(SignedOneDecimal, CultureInfo.InvariantCulture)})");
        }
    }

    private static void WriteCost(TextWriter writer, RunComparison comparison, PriceList? prices)
    {
        writer.WriteLine();
        writer.WriteLine("Tokens and cost:");
        foreach (var (label, run) in new[] { ("A", comparison.A), ("B", comparison.B) })
        {
            writer.WriteLine($"  {label}: chat {Describe(run.Tokens.Chat)}; judge {Describe(run.Tokens.Judge)}; {CostText(run, prices)}");
        }
    }

    private static string CostText(RunRecord run, PriceList? prices)
    {
        if (prices is null)
        {
            return "cost: no price file (evals/prices.json)";
        }

        var chat = prices.CostOf(run.Chat.Model, run.Tokens.Chat);
        var judge = prices.CostOf(run.Judge.Model, run.Tokens.Judge);
        var missing = new List<string>();
        if (chat is null)
        {
            missing.Add(run.Chat.Model);
        }

        if (judge is null)
        {
            missing.Add(run.Judge.Model);
        }

        return missing.Count > 0
            ? $"cost: no price for {string.Join(" and ", missing)}"
            : $"cost: {prices.Currency} {(chat!.Value + judge!.Value).ToString("F4", CultureInfo.InvariantCulture)} (chat {chat.Value.ToString("F4", CultureInfo.InvariantCulture)}, judge {judge.Value.ToString("F4", CultureInfo.InvariantCulture)})";
    }

    private static string Describe(TokenUsage usage) => $"{usage.Input} in ({usage.CachedInput} cached), {usage.Output} out";

    private static string Format(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
