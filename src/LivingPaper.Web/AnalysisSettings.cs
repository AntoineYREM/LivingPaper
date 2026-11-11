using System.Globalization;
using LivingPaper.Application;

namespace LivingPaper.Web;

// Reads the two settings of the analysis that can be changed from the environment: ANALYSIS_PAGES_PER_PART (how much of the
// book the model reads at once: lower it for a model with a small context) and ANALYSIS_PARALLEL_CALLS (how many calls at the
// same time: set 1 for a small local server or a strict provider). A value that is not usable stops the application at start,
// naming the variable, rather than being ignored.
public static class AnalysisSettings
{
    public const string PagesPerPartVariable = "ANALYSIS_PAGES_PER_PART";
    public const string ParallelCallsVariable = "ANALYSIS_PARALLEL_CALLS";

    public static AnalysisOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var defaults = new AnalysisOptions();
        return defaults with
        {
            PagesPerPart = ReadNumber(configuration, PagesPerPartVariable, defaults.PagesPerPart, 1, 1000),
            MaxParallelCalls = ReadNumber(configuration, ParallelCallsVariable, defaults.MaxParallelCalls, 1, 20),
        };
    }

    private static int ReadNumber(IConfiguration configuration, string name, int fallback, int min, int max)
    {
        var text = configuration[name];
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? value
            : throw new InvalidOperationException($"{name} must be a whole number from {min} to {max}.");
    }
}
