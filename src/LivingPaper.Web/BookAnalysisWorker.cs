using System.Collections.Concurrent;
using System.Threading.Channels;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Web;

// Analyzes the books in the background, one at a time, so the user can leave the page (PAPER-8). It is the queue behind
// IAnalysisScheduler: the pages put a book in it and the analysis runs here, in the same process.
// At startup it finds again what a restart cut: a book that was running is interrupted (and can be retried), the books that
// were waiting are queued again.
// It logs ids, codes and counts, never a title, a text of the book or a key.
public sealed partial class BookAnalysisWorker(IServiceProvider services, ILogger<BookAnalysisWorker> logger) : BackgroundService, IAnalysisScheduler
{
    private readonly Channel<(BookId Id, object Ticket)> _channel = Channel.CreateUnbounded<(BookId, object)>();

    // The books waiting or running here, each with the ticket of its place in the queue. A book taken out and queued again
    // gets a new ticket: the old place, still in the channel, is then skipped, and the end of the old run does not take
    // the new place away.
    private readonly ConcurrentDictionary<BookId, object> _queued = new();

    private readonly TaskCompletionSource _recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Lock _currentLock = new();
    private (BookId Id, CancellationTokenSource Stop, TaskCompletionSource Done)? _current;

    // Completes when the books cut by a restart have been found again (see ExecuteAsync). A test that creates books waits for
    // it, or the recovery could take a book it has just created for one left over from a previous run.
    public Task Recovered => _recovered.Task;

    public bool Enqueue(BookId bookId)
    {
        var ticket = new object();
        return _queued.TryAdd(bookId, ticket) && _channel.Writer.TryWrite((bookId, ticket));
    }

    public bool IsQueued(BookId bookId) => _queued.ContainsKey(bookId);

    public async Task CancelAsync(BookId bookId, CancellationToken cancellationToken)
    {
        _queued.TryRemove(bookId, out _);
        Task? stopped = null;
        lock (_currentLock)
        {
            if (_current is { } current && current.Id == bookId)
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

            await foreach (var (id, ticket) in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (_queued.TryGetValue(id, out var current) && ReferenceEquals(current, ticket))
                {
                    await RunAsync(id, ticket, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The application stops: the book that was running stays "running" and is interrupted at the next start.
        }
    }

    private async Task RunAsync(BookId id, object ticket, CancellationToken stoppingToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_currentLock)
        {
            _current = (id, stop, done);
        }

        try
        {
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

            _queued.TryRemove(new KeyValuePair<BookId, object>(id, ticket));
            done.TrySetResult();
        }
    }

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
