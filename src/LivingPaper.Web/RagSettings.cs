using System.Globalization;
using LivingPaper.Application;

namespace LivingPaper.Web;

// Reads the two settings of the search of passages in the chat: RAG_QUERY_MODE (rewrite, recent or last) and RAG_PASSAGES
// (how many passages the character gets). A value that is not usable stops the application at start, naming the variable.
public static class RagSettings
{
    public const string QueryModeVariable = "RAG_QUERY_MODE";
    public const string PassagesVariable = "RAG_PASSAGES";

    public static RagOptions Read(IConfiguration configuration, bool captureContent)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var defaults = new RagOptions { CaptureContent = captureContent };
        var mode = configuration[QueryModeVariable]?.Trim().ToLowerInvariant() switch
        {
            null or "" => defaults.Mode,
            "rewrite" => QueryMode.Rewrite,
            "recent" => QueryMode.Recent,
            "last" => QueryMode.Last,
            _ => throw new InvalidOperationException($"{QueryModeVariable} must be rewrite, recent or last."),
        };
        var text = configuration[PassagesVariable]?.Trim();
        var passages = string.IsNullOrEmpty(text) ? defaults.Passages
            : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= RagOptions.MaxPassages ? number
            : throw new InvalidOperationException($"{PassagesVariable} must be a whole number from 1 to {RagOptions.MaxPassages}.");
        return defaults with { Mode = mode, Passages = passages };
    }
}
