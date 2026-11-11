namespace LivingPaper.Web;

internal static partial class StartupLog
{
    // The problem names the file and the key; it never contains the text of a prompt, a book or a chat message.
    [LoggerMessage(Level = LogLevel.Error, Message = "The prompts file cannot be used until it is fixed: {Problem}")]
    public static partial void PromptsFileUnusable(this ILogger logger, string problem);
}
