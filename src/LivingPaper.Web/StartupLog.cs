namespace LivingPaper.Web;

internal static partial class StartupLog
{
    // The problem names the file and the key; it never contains the text of a prompt, a book or a chat message.
    [LoggerMessage(Level = LogLevel.Error, Message = "The prompts file cannot be used until it is fixed: {Problem}")]
    public static partial void PromptsFileUnusable(this ILogger logger, string problem);

    // An example that does not follow the rules is left out; the others are kept. The reason is a short technical text.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The example {File} is ignored: {Reason}.")]
    public static partial void ExampleIgnored(this ILogger logger, string file, string reason);

    // Titles are not logged, only the identifiers.
    [LoggerMessage(Level = LogLevel.Information, Message = "The address of the book {From} is now {To}.")]
    public static partial void BookAddressChanged(this ILogger logger, string from, string to);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} example(s) loaded.")]
    public static partial void ExamplesLoaded(this ILogger logger, int count);

    // PAPER-75. The address only (scheme, host, port): never the keys, which the address does not hold either.
    [LoggerMessage(Level = LogLevel.Information, Message = "Langfuse tracing is on: the calls to the language models are traced to {Address}. Prompts and replies are not recorded.")]
    public static partial void LangfuseTracingOnMetadataOnly(this ILogger logger, string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Langfuse tracing is on and LANGFUSE_CAPTURE_CONTENT is true: the prompts and replies of the calls, which hold the text of the books and of the conversations, are sent to {Address}.")]
    public static partial void LangfuseTracingOnWithContent(this ILogger logger, string address);
}
