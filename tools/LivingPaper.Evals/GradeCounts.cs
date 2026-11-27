namespace LivingPaper.Evals;

// How many replies got each grade, from 5 to 1. An average hides what changed: an invented fact (1) that becomes "I do not
// remember" (2) and a right answer (5) that becomes "I do not remember" move it in opposite ways.
internal static class GradeCounts
{
    public static string Describe(IEnumerable<EvalResult> results)
    {
        var counts = results.Where(result => result.Score.HasValue).CountBy(result => result.Score!.Value).ToDictionary();
        return string.Join(", ", Enumerable.Range(1, 5).Reverse().Select(grade => $"{grade}: {counts.GetValueOrDefault(grade)}"));
    }
}
