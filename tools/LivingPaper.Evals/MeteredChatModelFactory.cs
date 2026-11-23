using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// Counts the tokens of every call made through it. The chat of the application passes no usage meter to the model, so the tool hands
// its own to each call that gives none; the report then shows the tokens of the chat and of the judge separately.
internal sealed class MeteredChatModelFactory(IChatModelFactory inner, ChatUsageMeter meter) : IChatModelFactory
{
    public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true) =>
        new MeteredChatModel(inner.Create(provider, retryPassingFailures), meter);

    private sealed class MeteredChatModel(IChatModel model, ChatUsageMeter meter) : IChatModel
    {
        public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
            model.StreamReplyAsync(messages, usage ?? meter, cancellationToken);

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
            model.CompleteAsync(messages, usage ?? meter, cancellationToken);
    }
}
