using LivingPaper.Application;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Infrastructure.Tests;

// A fake IChatModel that plays a script: each call does the next scripted thing (a reply, a failure, a stream).
internal sealed class ScriptedChatModel : IChatModel
{
    private readonly Queue<Func<IAsyncEnumerable<string>>> _streams = new();
    private readonly Queue<Func<string>> _replies = new();

    public int Calls { get; private set; }

    public int StreamsDisposed { get; private set; }

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public ChatUsageMeter? LastUsage { get; private set; }

    public ScriptedChatModel ThenReply(string reply)
    {
        _replies.Enqueue(() => reply);
        return this;
    }

    public ScriptedChatModel ThenFail(Exception exception)
    {
        _replies.Enqueue(() => throw exception);
        return this;
    }

    public ScriptedChatModel ThenStream(params string[] pieces) => ThenStreamFailingAfter(pieces, null);

    public ScriptedChatModel ThenStreamFailingAfter(IReadOnlyList<string> pieces, Exception? exception)
    {
        _streams.Enqueue(() => Play(pieces, exception));
        return this;
    }

    public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        Record(messages, usage);
        return _streams.Dequeue()();
    }

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        Record(messages, usage);
        try
        {
            return Task.FromResult(_replies.Dequeue()());
        }
        catch (Exception exception)
        {
            return Task.FromException<string>(exception);
        }
    }

    private void Record(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage)
    {
        Calls++;
        LastMessages = messages;
        LastUsage = usage;
    }

    private async IAsyncEnumerable<string> Play(IReadOnlyList<string> pieces, Exception? exception)
    {
        try
        {
            foreach (var piece in pieces)
            {
                await Task.Yield();
                yield return piece;
            }

            if (exception is not null)
            {
                throw exception;
            }
        }
        finally
        {
            StreamsDisposed++;
        }
    }
}

// Keeps what the code under test logged, to check what is and is not written to the logs.
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Add((logLevel, formatter(state, exception)));
    }
}
