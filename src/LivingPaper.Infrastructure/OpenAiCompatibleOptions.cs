namespace LivingPaper.Infrastructure;

// How patient the client is with a language model. The defaults are long on purpose: a model that "thinks" before it
// answers can stay silent for minutes, and a book analysis is worth waiting for.
public sealed record OpenAiCompatibleOptions
{
    // Time to open the connection to the provider.
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);

    // Time between sending the request and receiving the start of the answer.
    public TimeSpan HeadersTimeout { get; init; } = TimeSpan.FromMinutes(5);

    // Time without any new event while a reply is streaming. Each event starts the count again.
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    // The waits before each retry of a call that failed for a passing reason: three retries.
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = RetryingChatModel.DefaultDelays;
}
