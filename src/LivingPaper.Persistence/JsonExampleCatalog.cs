using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The examples: every *.json file of a directory (examples/ in the repository), each in the format of a book of the library.
// A file is read like a file sent to be imported (it is untrusted: the schema, the sizes, the lengths of the texts, the
// passages and the vectors are validated), with two more rules for an example: it is an analyzed book (status Done, at least
// one character), and its identifier is not already used by another example. The name of the file is free
// (malade-imaginaire.json), unlike the files of the library, which are named after their identifier. A file that does not
// follow the rules is left out and listed with the reason: one bad example never hides the others.
// The directory is read once, the first time it is needed. The passages are large: they are not kept, and read again from
// their file when asked for.
public sealed class JsonExampleCatalog(string directory) : IExampleCatalog
{
    private const string StepsSuffix = ".steps.json";

    // The file of an example that has passages, and how many.
    private sealed record Passages(string Path, int Count);

    private sealed record Loaded(BookListing Listing, IReadOnlyDictionary<BookId, Passages> Passages);

    private readonly Lazy<Task<Loaded>> _loaded = new(() => LoadAsync(directory), LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<BookListing> ListAsync(CancellationToken cancellationToken) =>
        (await _loaded.Value.WaitAsync(cancellationToken).ConfigureAwait(false)).Listing;

    public async Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Books.FirstOrDefault(book => book.Id == id);

    public async Task<int> CountPassagesAsync(BookId id, CancellationToken cancellationToken) =>
        (await _loaded.Value.WaitAsync(cancellationToken).ConfigureAwait(false)).Passages.GetValueOrDefault(id)?.Count ?? 0;

    public async Task<PassageSet?> GetPassagesAsync(BookId id, CancellationToken cancellationToken)
    {
        var loaded = await _loaded.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return loaded.Passages.GetValueOrDefault(id) is { } kept ? (await ReadAsync(kept.Path).ConfigureAwait(false)).Passages : null;
    }

    private static async Task<Loaded> LoadAsync(string directory)
    {
        var books = new List<Book>();
        var unreadable = new List<UnreadableBook>();
        var passages = new Dictionary<BookId, Passages>();
        if (!Directory.Exists(directory))
        {
            return new Loaded(new BookListing(books, unreadable), passages);
        }

        var paths = Directory.EnumerateFiles(directory, "*.json")
            .Where(path => !path.EndsWith(StepsSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var (book, set, reason) = await ReadAsync(path).ConfigureAwait(false);
            if (book is null)
            {
                unreadable.Add(new UnreadableBook(name, reason));
            }
            else if (books.Exists(other => other.Id == book.Id))
            {
                unreadable.Add(new UnreadableBook(name, "another example has the same id"));
            }
            else
            {
                books.Add(book);
                if (set is not null)
                {
                    passages[book.Id] = new Passages(path, set.Passages.Count);
                }
            }
        }

        return new Loaded(new BookListing([.. books.OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)], unreadable), passages);
    }

    private static async Task<(Book? Book, PassageSet? Passages, string Reason)> ReadAsync(string path)
    {
        static (Book?, PassageSet?, string) Refused(string reason) => (null, null, reason);
        try
        {
            var length = new FileInfo(path).Length;
            if (length > BookService.MaxImportBytes)
            {
                return Refused("the file is too large");
            }

            var bytes = await SharedFile.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<BookFile>(bytes, BookFile.JsonOptions);
            if (file is null)
            {
                return Refused("the file is empty");
            }

            if (!JsonBookArchive.BookFitsItsLimit(file, bytes.Length, JsonBookRepository.MaxFileBytes))
            {
                return Refused("the file is too large");
            }

            // The id of an example is the one written in the file: its name is free.
            if (!BookId.TryParse(file.Id, out var id))
            {
                return Refused("the id in the file is not valid");
            }

            var book = file.ToBook(id, out var reason);
            if (book is null)
            {
                return Refused(reason);
            }

            if (book.Status != BookStatus.Done || book.Characters.Count == 0)
            {
                return Refused("an example must be an analyzed book: status Done and at least one character");
            }

            if (file.PassageIndex is null)
            {
                return (book, null, string.Empty);
            }

            var passages = file.PassageIndex.ToSet(out var field);
            return passages is null ? Refused($"wrong passages: {field}") : (book, passages, string.Empty);
        }
        catch (JsonException)
        {
            return Refused("the file is not valid JSON for a book");
        }
        catch (IOException)
        {
            return Refused("the file cannot be read");
        }
        catch (UnauthorizedAccessException)
        {
            return Refused("the file cannot be read");
        }
    }
}
