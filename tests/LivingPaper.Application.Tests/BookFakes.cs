using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Application.Tests;

// Keeps the books in memory, copying nothing: a test reads what the service saved.
internal sealed class InMemoryBookRepository : IBookRepository
{
    public Dictionary<BookId, Book> Books { get; } = [];

    public Exception? SaveFailure { get; set; }

    // Called at every save: a test counts the writes.
    public Action? OnSave { get; set; }

    // The calls give the processor back (Task.Yield), as a real disk would, so two requests at once really interleave.
    public async Task SaveAsync(Book book, CancellationToken cancellationToken)
    {
        await Task.Yield();
        OnSave?.Invoke();
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        lock (Books)
        {
            Books[book.Id] = book;
        }
    }

    public Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken) =>
        Task.FromResult(Books.GetValueOrDefault(id));

    public async Task<BookListing> ListAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (Books)
        {
            return new BookListing([.. Books.Values.OrderByDescending(book => book.SentAt)], []);
        }
    }

    public async Task<bool> DeleteAsync(BookId id, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (Books)
        {
            return Books.Remove(id);
        }
    }
}

internal sealed class InMemoryBookTextStore : IBookTextStore
{
    public Dictionary<BookId, string> Texts { get; } = [];

    public Task SaveAsync(BookId bookId, string text, CancellationToken cancellationToken)
    {
        Texts[bookId] = text;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(BookId bookId, CancellationToken cancellationToken) =>
        Task.FromResult(Texts.GetValueOrDefault(bookId));

    public Task DeleteAsync(BookId bookId, CancellationToken cancellationToken)
    {
        Texts.Remove(bookId);
        return Task.CompletedTask;
    }
}

// Answers with what the test says, whatever the file holds.
internal sealed class FakeBookTextExtractor(Func<Stream, BookText>? extract = null) : IBookTextExtractor
{
    public int Calls { get; private set; }

    public long LastLength { get; private set; }

    public BookText Extract(Stream content)
    {
        Calls++;
        LastLength = content.Length;
        return extract is null ? new BookText("The Lighthouse", "Lina climbs the steps.") : extract(content);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
