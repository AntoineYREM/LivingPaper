using LivingPaper.Application;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Infrastructure;

// Tries an embedding call again when it fails for a passing reason, with the waits and the rules of RetryingChatModel.
public sealed class RetryingEmbeddingModel(
    IEmbeddingModel inner,
    IReadOnlyList<TimeSpan> delays,
    ILogger<RetryingEmbeddingModel> logger,
    Func<TimeSpan, CancellationToken, Task>? wait = null) : IEmbeddingModel
{
    private readonly Func<TimeSpan, CancellationToken, Task> _wait = wait ?? ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await inner.EmbedAsync(texts, usage, cancellationToken).ConfigureAwait(false);
            }
            catch (ChatProviderException exception) when (exception.IsTransient && attempt <= delays.Count)
            {
                RetryingChatModel.LogRetry(logger, exception.Kind, exception.StatusCode, attempt + 1, delays.Count + 1, delays[attempt - 1].TotalSeconds);
                await _wait(delays[attempt - 1], cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
