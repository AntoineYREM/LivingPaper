namespace LivingPaper.Web;

internal static partial class StartupLog
{
    // The problem names the file and the key; it never contains the text of a prompt, a book or a chat message.
    [LoggerMessage(Level = LogLevel.Error, Message = "The prompts file cannot be used until it is fixed: {Problem}")]
    public static partial void PromptsFileUnusable(this ILogger logger, string problem);

    // An example that does not follow the rules is left out; the others are kept. The reason is a short technical text.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The example {File} is ignored: {Reason}.")]
    public static partial void ExampleIgnored(this ILogger logger, string file, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} example(s) loaded.")]
    public static partial void ExamplesLoaded(this ILogger logger, int count);
}
