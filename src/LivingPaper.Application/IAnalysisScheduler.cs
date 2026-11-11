using LivingPaper.Domain;

namespace LivingPaper.Application;

// The queue of books waiting to be analyzed, one at a time, run by a background service. A port so the use cases of the
// books do not depend on the service that runs the analysis, and so tests can replace it.
public interface IAnalysisScheduler
{
    // Adds the book to the queue. False when it is already waiting or running.
    bool Enqueue(BookId bookId);

    // True while the book is waiting in the queue or being analyzed by this application. A book that says it is waiting or
    // running but is not here was stopped by a restart: it is interrupted.
    bool IsQueued(BookId bookId);

    // Takes the book out of the queue and, when it is being analyzed, stops that analysis and waits until it has stopped,
    // so nothing is written for a book that is about to be deleted.
    Task CancelAsync(BookId bookId, CancellationToken cancellationToken);
}
