using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// One JSON file per book: {booksDirectory}/{id}.json. Easy to read, copy, back up and export.
public sealed class JsonBookRepository(string booksDirectory) : IBookRepository
{
    // A book with 50 characters of the maximum size is under 200 KB. Anything much bigger is not a book of ours.
    public const int MaxFileBytes = 1_048_576;

    private const string Extension = ".json";
    private const string StepsSuffix = ".steps.json";

    public async Task SaveAsync(Book book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        var violations = book.FindLimitViolations();
        if (violations.Count > 0)
        {
            throw new ArgumentException($"The book has texts longer than allowed: {string.Join(", ", violations)}.", nameof(book));
        }

        var content = JsonSerializer.SerializeToUtf8Bytes(BookFile.From(book), BookFile.JsonOptions);
        await AtomicFile.WriteAsync(PathOf(book.Id), content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken)
    {
        var path = PathOf(id);
        if (!File.Exists(path))
        {
            return null;
        }

        return await ReadAsync(path, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BookListing> ListAsync(CancellationToken cancellationToken)
    {
        var books = new List<Book>();
        var unreadable = new List<UnreadableBook>();
        if (!Directory.Exists(booksDirectory))
        {
            return new BookListing(books, unreadable);
        }

        foreach (var path in Directory.EnumerateFiles(booksDirectory, "*" + Extension))
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(Extension, StringComparison.Ordinal)
                || name.EndsWith(StepsSuffix, StringComparison.Ordinal)
                || !BookId.TryParse(name[..^Extension.Length], out var id))
            {
                continue;
            }

            try
            {
                books.Add(await ReadAsync(path, id, cancellationToken).ConfigureAwait(false));
            }
            catch (BookUnreadableException exception)
            {
                unreadable.Add(new UnreadableBook(name, exception.Reason));
            }
        }

        return new BookListing([.. books.OrderByDescending(book => book.SentAt)], unreadable);
    }

    public async Task<bool> DeleteAsync(BookId id, CancellationToken cancellationToken)
    {
        var path = PathOf(id);
        var existed = File.Exists(path);
        await SharedFile.DeleteIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        await SharedFile.DeleteIfExistsAsync(Path.Combine(booksDirectory, id + StepsSuffix), cancellationToken).ConfigureAwait(false);
        return existed;
    }

    private string PathOf(BookId id) => Path.Combine(booksDirectory, id + Extension);

    private static async Task<Book> ReadAsync(string path, BookId id, CancellationToken cancellationToken)
    {
        var storageName = Path.GetFileName(path);
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new BookUnreadableException(storageName, "the file is too large");
        }

        BookFile? file;
        try
        {
            var bytes = await SharedFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            file = JsonSerializer.Deserialize<BookFile>(bytes, BookFile.JsonOptions);
        }
        catch (JsonException)
        {
            throw new BookUnreadableException(storageName, "the file is not valid JSON for a book");
        }

        var book = file?.ToBook(id, out var reason);
        return book ?? throw new BookUnreadableException(storageName, file is null ? "the file is empty" : ReasonOf(file, id));
    }

    private static string ReasonOf(BookFile file, BookId id)
    {
        file.ToBook(id, out var reason);
        return reason;
    }
}
