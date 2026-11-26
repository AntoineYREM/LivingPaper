using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Application.Tests;

// Keeps the books in memory, copying nothing: a test reads what the service saved.
internal sealed class InMemoryBookRepository : IBookRepository
{
    public Dictionary<BookId, Book> Books { get; } = [];

    // Files that exist but cannot be read: listed as such.
    public List<UnreadableBook> Unreadable { get; } = [];

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
            return new BookListing([.. Books.Values.OrderByDescending(book => book.SentAt)], [.. Unreadable]);
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

// The examples, in memory.
internal sealed class InMemoryExampleCatalog(params Book[] examples) : IExampleCatalog
{
    public Task<BookListing> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BookListing([.. examples.OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)], []));

    public Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken) =>
        Task.FromResult(examples.FirstOrDefault(book => book.Id == id));

    // The passages of the examples that have some.
    public Dictionary<BookId, PassageSet> Passages { get; } = [];

    public Task<int> CountPassagesAsync(BookId id, CancellationToken cancellationToken) =>
        Task.FromResult(Passages.GetValueOrDefault(id)?.Passages.Count ?? 0);

    public Task<PassageSet?> GetPassagesAsync(BookId id, CancellationToken cancellationToken) =>
        Task.FromResult(Passages.GetValueOrDefault(id));
}

// An archive that does not know a file format: it remembers the books it exported, and gives them back when it is handed the
// same bytes. Any other bytes are refused as not JSON, unless the test says what they are (As).
internal sealed class FakeBookArchive : IBookArchive
{
    private readonly Dictionary<string, Book> _exported = [];
    private readonly Dictionary<string, ArchiveParseResult> _scripted = [];
    private readonly Dictionary<string, PassageSet> _passages = [];

    // What the next export with passages says it left out.
    public ExportOmission Omission { get; set; }

    // The sets of passages that were exported.
    public List<PassageSet> ExportedPassages { get; } = [];

    public byte[] Export(Book book)
    {
        var key = "exported:" + Guid.NewGuid().ToString("N");
        _exported[key] = Copy(book); // As a file: what the book was when it was exported.
        return System.Text.Encoding.UTF8.GetBytes(key);
    }

    public ArchivedBook ExportWithPassages(Book book, PassageSet passages)
    {
        var content = Export(book);
        ExportedPassages.Add(passages);
        _passages[System.Text.Encoding.UTF8.GetString(content)] = passages;
        return new ArchivedBook(content, Omission);
    }

    public ArchiveParseResult Parse(byte[] content)
    {
        var key = System.Text.Encoding.UTF8.GetString(content);
        if (_scripted.TryGetValue(key, out var scripted))
        {
            return scripted;
        }

        return _exported.TryGetValue(key, out var book)
            ? new ArchiveParseResult(Copy(book), null, Passages: _passages.GetValueOrDefault(key))
            : new ArchiveParseResult(null, ImportRejection.NotJson);
    }

    // The bytes that the archive will read as this result.
    public byte[] As(ArchiveParseResult result)
    {
        var key = "scripted:" + Guid.NewGuid().ToString("N");
        _scripted[key] = result;
        return System.Text.Encoding.UTF8.GetBytes(key);
    }

    // What reading a file gives: a book of its own, never the object that was exported.
    private static Book Copy(Book source)
    {
        var copy = new Book(source.Id, source.Title, source.FileName, source.SentAt)
        {
            Summary = source.Summary,
            StorySummary = source.StorySummary,
            StorySummaryPartial = source.StorySummaryPartial,
            PlotSummary = source.PlotSummary,
            Status = source.Status,
            PartsDone = source.PartsDone,
            PartsTotal = source.PartsTotal,
            ProfilesDone = source.ProfilesDone,
            ProfilesTotal = source.ProfilesTotal,
            PartsSkipped = source.PartsSkipped,
            ProviderName = source.ProviderName,
            Model = source.Model,
            InputTokens = source.InputTokens,
            CachedInputTokens = source.CachedInputTokens,
            OutputTokens = source.OutputTokens,
            StartedAt = source.StartedAt,
            UpdatedAt = source.UpdatedAt,
            FinishedAt = source.FinishedAt,
        };
        copy.Characters.AddRange(source.Characters);
        return copy;
    }
}
