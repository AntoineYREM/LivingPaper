using System.Collections.Concurrent;
using System.Threading.Channels;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Web;

// Analyzes the books in the background, one at a time, so the user can leave the page (PAPER-8). It is the queue behind
// IAnalysisScheduler: the pages put a book in it and the analysis runs here, in the same process. The same queue indexes
// the passages of a book already analyzed or of an example (PAPER-85), and checks the vectors that came with the file of a
// book (PAPER-88).
// At startup it finds again what a restart cut: a book that was running is interrupted (and can be retried), the books that
// were waiting are queued again.
// It logs ids, codes and counts, never a title, a text of the book or a key.
public sealed partial class BookAnalysisWorker(IServiceProvider services, ILogger<BookAnalysisWorker> logger) : BackgroundService, IAnalysisScheduler
{
    private readonly Channel<(BookKey Key, Ticket Ticket)> _channel = Channel.CreateUnbounded<(BookKey, Ticket)>();

    // The books waiting or running here, each with the ticket of its place in the queue. A book taken out and queued again
    // gets a new ticket: the old place, still in the channel, is then skipped, and the end of the old run does not take
    // the new place away.
    private readonly ConcurrentDictionary<BookKey, Ticket> _queued = new();

    private enum Work
    {
        Analysis,
        Indexing,
        Check,
    }

    // A place in the queue, and what to do there. Compared by reference: two places are never the same one.
    private sealed class Ticket(Work work)
    {
        public Work Work { get; } = work;
    }

    private readonly TaskCompletionSource _recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Lock _currentLock = new();
    private (BookKey Key, CancellationTokenSource Stop, TaskCompletionSource Done)? _current;

    // Completes when the books cut by a restart have been found again (see ExecuteAsync). A test that creates books waits for
    // it, or the recovery could take a book it has just created for one left over from a previous run.
    public Task Recovered => _recovered.Task;

    public bool Enqueue(BookId bookId) => Add(Key(bookId, BookShelf.Library), new Ticket(Work.Analysis));

    public bool EnqueueIndexing(BookId bookId, BookShelf shelf = BookShelf.Library) => Add(Key(bookId, shelf), new Ticket(Work.Indexing));

    public bool EnqueueCheck(BookId bookId) => Add(Key(bookId, BookShelf.Library), new Ticket(Work.Check));

    private static BookKey Key(BookId bookId, BookShelf shelf) => new(shelf, bookId.ToString());

    private bool Add(BookKey key, Ticket ticket) => _queued.TryAdd(key, ticket) && _channel.Writer.TryWrite((key, ticket));

    public bool IsQueued(BookId bookId, BookShelf shelf = BookShelf.Library) => _queued.ContainsKey(Key(bookId, shelf));

    // Only a book of the library is ever taken out: an example cannot be deleted.
    public async Task CancelAsync(BookId bookId, CancellationToken cancellationToken)
    {
        var key = Key(bookId, BookShelf.Library);
        _queued.TryRemove(key, out _);
        Task? stopped = null;
        lock (_currentLock)
        {
            if (_current is { } current && current.Key == key)
            {
                current.Stop.Cancel();
                stopped = current.Done.Task;
            }
        }

        if (stopped is not null)
        {
            await stopped.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield first so the application finishes starting before the work begins.
        await Task.Yield();
        try
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BookService>().RecoverAsync(stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _recovered.TrySetResult();
            }

            await foreach (var (key, ticket) in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (_queued.TryGetValue(key, out var current) && ReferenceEquals(current, ticket))
                {
                    await RunAsync(key, ticket, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The application stops: the book that was running stays "running" and is interrupted at the next start.
        }
    }

    private async Task RunAsync(BookKey key, Ticket ticket, CancellationToken stoppingToken)
    {
        // The queue only holds keys made from the identifier of a book.
        if (!BookId.TryParse(key.Id, out var id))
        {
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_currentLock)
        {
            _current = (key, stop, done);
        }

        try
        {
            if (ticket.Work != Work.Analysis)
            {
                LogIndexingStarted(logger, id);
                var books = services.GetRequiredService<BookService>();
                await (ticket.Work == Work.Check ? books.CheckVectorsAsync(id, stop.Token)
                    : key.Shelf == BookShelf.Examples ? books.IndexExampleAsync(id, stop.Token)
                    : books.IndexAsync(id, stop.Token)).ConfigureAwait(false);
                LogIndexingFinished(logger, id);
                return;
            }

            LogStarted(logger, id);
            var failure = await services.GetRequiredService<BookAnalyzer>().AnalyzeAsync(id, stop.Token).ConfigureAwait(false);
            if (failure is null)
            {
                LogFinished(logger, id);
            }
            else
            {
                LogFailed(logger, id, failure.Value);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The book was deleted or retried while it ran: nothing more to do for it.
            LogStopped(logger, id);
        }
        catch (Exception exception)
        {
            // The analysis turns its own failures into the book's state; this is a disk or a bug. Only the kind of error:
            // its message could hold a passage of the book.
            LogCrashed(logger, id, exception.GetType().Name);
        }
        finally
        {
            lock (_currentLock)
            {
                _current = null;
            }

            _queued.TryRemove(new KeyValuePair<BookKey, Ticket>(key, ticket));
            done.TrySetResult();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The indexing of book {BookId} started.")]
    private static partial void LogIndexingStarted(ILogger logger, BookId bookId);

    // A failure is written on the book, not here.
    [LoggerMessage(Level = LogLevel.Information, Message = "The indexing of book {BookId} ended.")]
    private static partial void LogIndexingFinished(ILogger logger, BookId bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The analysis of book {BookId} started.")]
    private static partial void LogStarted(ILogger logger, BookId bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The analysis of book {BookId} is done.")]
    private static partial void LogFinished(ILogger logger, BookId bookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The analysis of book {BookId} stopped: {Failure}.")]
    private static partial void LogFailed(ILogger logger, BookId bookId, AnalysisFailure failure);

    [LoggerMessage(Level = LogLevel.Information, Message = "The analysis of book {BookId} was stopped.")]
    private static partial void LogStopped(ILogger logger, BookId bookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The analysis of book {BookId} could not be recorded: {Error}.")]
    private static partial void LogCrashed(ILogger logger, BookId bookId, string error);
}
