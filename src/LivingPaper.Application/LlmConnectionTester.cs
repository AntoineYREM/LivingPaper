using LivingPaper.Domain;

namespace LivingPaper.Application;

// Sends one short message (the prompt llm.connectionTest of the prompts file, "Reply with the single word: pong" by
// default) and reads what comes back: a reply for a chat model, a vector for an embedding model. One try only and a time
// limit: a wrong address must show its error at once, not after the retries of a real call. Any usable answer means the
// connection works; what it says does not matter.
public sealed class LlmConnectionTester(
    IChatModelFactory factory, IEmbeddingModelFactory embeddings, IPromptProvider prompts, TimeSpan? timeout = null) : ILlmConnectionTester
{
    // A model that thinks before answering can take a while; a dead address is reported long before this.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();

    public Task<ConnectionTestResult> TestAsync(LlmProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return RunAsync(
            async (message, limit) =>
            {
                // Its own trace (PAPER-75): the call is not part of any analysis or chat.
                using var span = LlmTelemetry.Source.StartActivity("connection-test");
                var model = factory.Create(provider, retryPassingFailures: false);
                await model.CompleteAsync([new ChatMessage(ChatRole.User, message)], usage: null, limit).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<ConnectionTestResult> TestEmbeddingAsync(LlmProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return RunAsync(
            async (message, limit) =>
            {
                var model = embeddings.Create(provider, retryPassingFailures: false);
                await model.EmbedAsync([message], usage: null, limit).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private async Task<ConnectionTestResult> RunAsync(Func<string, CancellationToken, Task> call, CancellationToken cancellationToken)
    {
        string message;
        try
        {
            message = await prompts.RenderAsync(PromptKeys.ConnectionTest, NoVariables, cancellationToken).ConfigureAwait(false);
        }
        catch (PromptFileException exception)
        {
            return ConnectionTestResult.Failed(ConnectionFailure.PromptFile, detail: exception.Message);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await call(message, limit.Token).ConfigureAwait(false);
            return ConnectionTestResult.Ok();
        }
        catch (ChatProviderException exception)
        {
            return Classify(exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The time limit of the test, not the user giving up.
            return ConnectionTestResult.Failed(ConnectionFailure.Unreachable);
        }
        catch (ArgumentException)
        {
            // The client refuses the address. The validator normally stops this earlier; no detail, it could repeat the URL.
            return ConnectionTestResult.Failed(ConnectionFailure.Unreachable);
        }
    }

    private static ConnectionTestResult Classify(ChatProviderException exception) => exception.Kind switch
    {
        ChatFailureKind.Unreachable or ChatFailureKind.TimedOut => ConnectionTestResult.Failed(ConnectionFailure.Unreachable),
        ChatFailureKind.Http => exception.StatusCode switch
        {
            401 or 403 => ConnectionTestResult.Failed(ConnectionFailure.KeyRefused, exception.StatusCode),
            400 or 404 => ConnectionTestResult.Failed(ConnectionFailure.ModelUnknown, exception.StatusCode),
            _ => ConnectionTestResult.Failed(ConnectionFailure.ProviderError, exception.StatusCode),
        },
        _ => ConnectionTestResult.Failed(ConnectionFailure.InvalidResponse),
    };
}
