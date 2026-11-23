using System.Globalization;

namespace LivingPaper.Evals;

// The average of a category over the questions both runs answered, one number for each run.
internal sealed record CategoryDelta(string Category, int Questions, double AverageA, double AverageB)
{
    public double Delta => AverageB - AverageA;
}

// One question: the mean of its tries in each run.
internal sealed record QuestionDelta(string QuestionId, string Category, double MeanA, double MeanB)
{
    public double Delta => MeanB - MeanA;
}

// The mean of the differences (B - A) over the questions, with the interval in which the real mean difference is, in 95 cases out of 100
// if the questions are a fair sample. N is the number of questions: the tries of one question are not independent, so they are averaged first.
internal sealed record Interval(double Mean, double Low, double High, int N)
{
    public bool ExcludesZero => Low > 0 || High < 0;
}

// Two runs side by side. A is the baseline, B the candidate: a positive difference means B did better.
// Only the questions that both runs have graded are compared, so a failed try never turns into a worse score. Each question counts once:
// the tries of a question are averaged, then the differences of the questions are what is averaged and given an interval (a paired
// comparison, which does not suffer from the questions being easier or harder than each other).
internal sealed record RunComparison(
    RunRecord A,
    RunRecord B,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CategoryDelta> Categories,
    IReadOnlyList<QuestionDelta> Questions,
    Interval? Overall)
{
    // Two-sided 95% critical values of Student's t for 1 to 30 degrees of freedom; beyond, the normal 1.96 is close enough.
    private static readonly double[] StudentT95 =
    [
        12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228,
        2.201, 2.179, 2.160, 2.145, 2.131, 2.120, 2.110, 2.101, 2.093, 2.086,
        2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042,
    ];

    public static RunComparison Compare(RunRecord a, RunRecord b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var meansA = MeansByQuestion(a);
        var meansB = MeansByQuestion(b);
        var questions = a.Results.Select(result => result.QuestionId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => meansA.ContainsKey(id) && meansB.ContainsKey(id))
            .Select(id => new QuestionDelta(id, meansA[id].Category, meansA[id].Mean, meansB[id].Mean))
            .ToList();

        var categoryOrder = a.Results.Select(result => result.Category).Concat(b.Results.Select(result => result.Category)).Distinct(StringComparer.Ordinal);
        var categories = categoryOrder
            .Select(category => (Category: category, Questions: questions.Where(question => question.Category == category).ToList()))
            .Where(group => group.Questions.Count > 0)
            .Select(group => new CategoryDelta(group.Category, group.Questions.Count, group.Questions.Average(q => q.MeanA), group.Questions.Average(q => q.MeanB)))
            .ToList();

        return new RunComparison(a, b, WarningsFor(a, b, questions), categories, questions, IntervalOf(questions));
    }

    // The questions where the runs differ most, the biggest difference first, in either direction.
    public static IReadOnlyList<QuestionDelta> Biggest(IReadOnlyList<QuestionDelta> questions, int count) =>
        [.. questions.OrderByDescending(question => Math.Abs(question.Delta)).ThenBy(question => question.QuestionId, StringComparer.Ordinal).Take(count)];

    private static Dictionary<string, (string Category, double Mean)> MeansByQuestion(RunRecord run) =>
        run.Results
            .Where(result => result.Score.HasValue)
            .GroupBy(result => result.QuestionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (group.First().Category, group.Average(result => (double)result.Score!.Value)), StringComparer.Ordinal);

    private static Interval? IntervalOf(List<QuestionDelta> questions)
    {
        if (questions.Count < 2)
        {
            return null;
        }

        var differences = questions.Select(question => question.Delta).ToList();
        var mean = differences.Average();
        var variance = differences.Sum(difference => (difference - mean) * (difference - mean)) / (differences.Count - 1);
        var standardError = Math.Sqrt(variance / differences.Count);
        var degrees = differences.Count - 1;
        var critical = degrees <= StudentT95.Length ? StudentT95[degrees - 1] : 1.96;
        return new Interval(mean, mean - (critical * standardError), mean + (critical * standardError), differences.Count);
    }

    private static List<string> WarningsFor(RunRecord a, RunRecord b, List<QuestionDelta> common)
    {
        var warnings = new List<string>();
        if (a.Dataset != b.Dataset)
        {
            warnings.Add($"The runs used different datasets ({a.Dataset} and {b.Dataset}): the comparison is not meaningful.");
        }

        if (a.Character != b.Character)
        {
            warnings.Add($"The runs are about different characters ({a.Character} and {b.Character}).");
        }

        if (a.Judge.PromptVersion != b.Judge.PromptVersion)
        {
            warnings.Add($"The judge prompt versions differ ({a.Judge.PromptVersion} and {b.Judge.PromptVersion}): the grades are not comparable.");
        }

        if (a.Judge.Model != b.Judge.Model)
        {
            warnings.Add($"The judges differ ({a.Judge.Model} and {b.Judge.Model}): a difference can come from the judge, not from the character.");
        }

        if (a.Repeats != b.Repeats)
        {
            warnings.Add($"The questions were asked a different number of times each ({a.Repeats} and {b.Repeats}): the run with fewer tries is noisier.");
        }

        var asked = a.Results.Select(result => result.QuestionId).Union(b.Results.Select(result => result.QuestionId), StringComparer.Ordinal).Count();
        if (common.Count < asked)
        {
            warnings.Add($"{(asked - common.Count).ToString(CultureInfo.InvariantCulture)} of {asked.ToString(CultureInfo.InvariantCulture)} questions were not graded in both runs and are left out of the comparison.");
        }

        return warnings;
    }
}
