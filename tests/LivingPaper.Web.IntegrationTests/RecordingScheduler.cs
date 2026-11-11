using System.Collections.Concurrent;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Web.IntegrationTests;

// A queue of analyses that only remembers what it was given: nothing runs.
public sealed class RecordingScheduler : IAnalysisScheduler
{
    private readonly ConcurrentDictionary<BookId, bool> _queued = new();

    public IReadOnlyCollection<BookId> Queued => [.. _queued.Keys];

    public bool Enqueue(BookId bookId) => _queued.TryAdd(bookId, true);

    public bool IsQueued(BookId bookId) => _queued.ContainsKey(bookId);

    public Task CancelAsync(BookId bookId, CancellationToken cancellationToken)
    {
        _queued.TryRemove(bookId, out _);
        return Task.CompletedTask;
    }
}
