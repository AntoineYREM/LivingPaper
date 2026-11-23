namespace LivingPaper.Infrastructure;

// What the tracing of the language model calls may record.
public sealed record LlmTracingOptions
{
    // False (the default): only the model, the tokens, the duration and the outcome of a call. True: also the messages sent and
    // the reply, which hold the text of the books and of the conversations. Set with LANGFUSE_CAPTURE_CONTENT.
    public bool CaptureContent { get; init; }
}
