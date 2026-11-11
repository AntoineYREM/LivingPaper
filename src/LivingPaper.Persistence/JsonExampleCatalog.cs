using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The examples: every *.json file of a directory (examples/ in the repository), each in the format of a book of the library.
// A file is read like any book read from disk (it is untrusted: the schema, the sizes and the lengths of the texts are
// validated), with two more rules for an example: it is an analyzed book (status Done, at least one character), and its
// identifier is not already used by another example. The name of the file is free (malade-imaginaire.json), unlike the files of
// the library, which are named after their identifier. A file that does not follow the rules is left out and listed with the
// reason: one bad example never hides the others.
// The directory is read once, the first time it is needed.
public sealed class JsonExampleCatalog(string directory) : IExampleCatalog
{
    private const string StepsSuffix = ".steps.json";

    private readonly Lazy<Task<BookListing>> _listing = new(() => LoadAsync(directory), LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<BookListing> ListAsync(CancellationToken cancellationToken) => _listing.Value.WaitAsync(cancellationToken);

    public async Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Books.FirstOrDefault(book => book.Id == id);

    private static async Task<BookListing> LoadAsync(string directory)
    {
        var books = new List<Book>();
        var unreadable = new List<UnreadableBook>();
        if (!Directory.Exists(directory))
        {
            return new BookListing(books, unreadable);
        }

        var paths = Directory.EnumerateFiles(directory, "*.json")
            .Where(path => !path.EndsWith(StepsSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var (book, reason) = await ReadAsync(path).ConfigureAwait(false);
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
            }
        }

        return new BookListing([.. books.OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)], unreadable);
    }

    private static async Task<(Book? Book, string Reason)> ReadAsync(string path)
    {
        try
        {
            if (new FileInfo(path).Length > JsonBookRepository.MaxFileBytes)
            {
                return (null, "the file is too large");
            }

            var bytes = await SharedFile.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<BookFile>(bytes, BookFile.JsonOptions);
            if (file is null)
            {
                return (null, "the file is empty");
            }

            // The id of an example is the one written in the file: its name is free.
            if (!BookId.TryParse(file.Id, out var id))
            {
                return (null, "the id in the file is not valid");
            }

            var book = file.ToBook(id, out var reason);
            if (book is null)
            {
                return (null, reason);
            }

            return book.Status == BookStatus.Done && book.Characters.Count > 0
                ? (book, string.Empty)
                : (null, "an example must be an analyzed book: status Done and at least one character");
        }
        catch (JsonException)
        {
            return (null, "the file is not valid JSON for a book");
        }
        catch (IOException)
        {
            return (null, "the file cannot be read");
        }
        catch (UnauthorizedAccessException)
        {
            return (null, "the file cannot be read");
        }
    }
}
