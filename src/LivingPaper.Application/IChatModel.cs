namespace LivingPaper.Application;

// A language model reached through the OpenAI-compatible Chat Completions API. The only way the application talks to an
// AI; faked in tests, which never call a real provider. Both methods throw ChatProviderException on failure, after
// retrying the passing failures (see ChatProviderException.IsTransient).
public interface IChatModel
{
    // The reply piece by piece, as the model writes it (the chat shows it as it comes).
    // A passing failure is retried only before the first piece: after that, text has already been handed out. A reply
    // that ends before its end marker is an error, never a shorter reply.
    // usage: when given, receives the tokens of the call if the provider reports them.
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken);

    // The whole reply (the analysis). Retried as a whole, including a reply cut in the middle.
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken);
}
