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

    // The limits of one reply, against a provider that would fill the memory or never end. They are far above any reply the
    // application asks for (the longest, the story of a book, is about 24,000 characters); going over one is not retried.

    // The longest line of the stream, in bytes: an event holds a few words.
    public int MaxLineBytes { get; init; } = 1024 * 1024;

    // The longest text of a whole reply, in characters.
    public int MaxReplyLength { get; init; } = 1_000_000;

    // The longest time from sending the request to the end of the reply, even when events keep coming.
    public TimeSpan MaxReplyDuration { get; init; } = TimeSpan.FromMinutes(30);

    // A ceiling on the length of a reply ("max_tokens"), counting the tokens a model spends thinking. Null (the default) sends none. Some
    // providers, such as OpenRouter, set aside the cost of the longest possible reply before answering: with no ceiling, an expensive
    // model can be refused (HTTP 402) by a key that has a spending limit. Used by the evaluation tool; the site sends none.
    // OpenAI itself refuses "max_tokens" for its newest models: do not set it for those.
    public int? MaxOutputTokens { get; init; }

    // The waits before each retry of a call that failed for a passing reason: three retries.
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = RetryingChatModel.DefaultDelays;
}
