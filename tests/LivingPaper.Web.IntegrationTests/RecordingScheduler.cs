using System.Collections.Concurrent;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Web.IntegrationTests;

// A queue of analyses that only remembers what it was given: nothing runs.
public sealed class RecordingScheduler : IAnalysisScheduler
{
    private readonly ConcurrentDictionary<(BookShelf Shelf, BookId Id), bool> _queued = new();

    // The books of the library waiting here.
    public IReadOnlyCollection<BookId> Queued => [.. _queued.Keys.Where(key => key.Shelf == BookShelf.Library).Select(key => key.Id)];

    public bool Enqueue(BookId bookId) => _queued.TryAdd((BookShelf.Library, bookId), true);

    // The books queued to have their passages indexed.
    public ConcurrentBag<BookId> Indexing { get; } = [];

    public bool EnqueueIndexing(BookId bookId, BookShelf shelf = BookShelf.Library)
    {
        if (!_queued.TryAdd((shelf, bookId), true))
        {
            return false;
        }

        Indexing.Add(bookId);
        return true;
    }

    public bool EnqueueCheck(BookId bookId) => Enqueue(bookId);

    public bool IsQueued(BookId bookId, BookShelf shelf = BookShelf.Library) => _queued.ContainsKey((shelf, bookId));

    public Task CancelAsync(BookId bookId, CancellationToken cancellationToken)
    {
        _queued.TryRemove((BookShelf.Library, bookId), out _);
        return Task.CompletedTask;
    }
}
