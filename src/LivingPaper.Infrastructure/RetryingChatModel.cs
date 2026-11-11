using System.Runtime.CompilerServices;
using LivingPaper.Application;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Infrastructure;

// Tries a language model call again when it fails for a passing reason (see ChatProviderException.IsTransient): after
// each wait of "delays" (2, 5 then 10 seconds by default), so a call is made at most four times. A refusal (400, 401,
// 403, 404), an unreadable answer or a content filter would answer the same again and are thrown at once.
public sealed partial class RetryingChatModel(
    IChatModel inner,
    IReadOnlyList<TimeSpan> delays,
    ILogger<RetryingChatModel> logger,
    Func<TimeSpan, CancellationToken, Task>? wait = null) : IChatModel
{
    public static IReadOnlyList<TimeSpan> DefaultDelays { get; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private readonly Func<TimeSpan, CancellationToken, Task> _wait = wait ?? ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await inner.CompleteAsync(messages, usage, cancellationToken).ConfigureAwait(false);
            }
            catch (ChatProviderException exception) when (exception.IsTransient && attempt <= delays.Count)
            {
                LogRetry(logger, exception.Kind, exception.StatusCode, attempt + 1, delays.Count + 1, delays[attempt - 1].TotalSeconds);
                await _wait(delays[attempt - 1], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // A try is repeated only while nothing was handed out: once a piece went to the reader, a second reply would be
    // glued to the first one. After that, a failure ends the stream with its error.
    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var enumerator = inner.StreamReplyAsync(messages, usage, cancellationToken).GetAsyncEnumerator(cancellationToken);
            await using (enumerator.ConfigureAwait(false))
            {
                var delivered = false;
                var finished = false;
                var failedForAPassingReason = false;
                while (!finished && !failedForAPassingReason)
                {
                    string? piece = null;
                    try
                    {
                        finished = !await enumerator.MoveNextAsync().ConfigureAwait(false);
                        if (!finished)
                        {
                            piece = enumerator.Current;
                        }
                    }
                    catch (ChatProviderException exception) when (!delivered && exception.IsTransient && attempt <= delays.Count)
                    {
                        LogRetry(logger, exception.Kind, exception.StatusCode, attempt + 1, delays.Count + 1, delays[attempt - 1].TotalSeconds);
                        failedForAPassingReason = true;
                    }

                    if (piece is not null)
                    {
                        delivered = true;
                        yield return piece;
                    }
                }

                if (finished)
                {
                    yield break;
                }
            }

            await _wait(delays[attempt - 1], cancellationToken).ConfigureAwait(false);
        }
    }

    // Only the kind of failure and the HTTP status are logged: never an answer, a book or a message.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The language model call failed ({Kind}, HTTP status {Status}); try {Next} of {Total} in {Seconds} s.")]
    private static partial void LogRetry(ILogger logger, ChatFailureKind kind, int? status, int next, int total, double seconds);
}
