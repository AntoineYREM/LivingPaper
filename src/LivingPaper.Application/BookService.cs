using System.Text;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// The use cases of sending and deleting a book. A book is read at once (so a scanned PDF or a damaged file is refused in
// front of the user, not minutes later), created waiting for its analysis, and its text is kept apart until the analysis
// has finished.
// Sending is done one at a time (a lock around the check for the same title and the save), so two sends of the same title
// at once cannot both succeed.
public sealed class BookService(IBookRepository books, IBookTextStore texts, IBookTextExtractor extractor, TimeProvider clock) : IDisposable
{
    // The largest book file accepted: 20 MB.
    public const long MaxBookBytes = 20L * 1024 * 1024;

    // The title of a book whose file and sender give none.
    public const string UntitledBook = "Untitled book";

    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task<BookListing> ListAsync(CancellationToken cancellationToken) => books.ListAsync(cancellationToken);

    // content: the file as sent, which need not be seekable; at most MaxBookBytes are read. title: the title the user
    // typed, if any; else the one of the file; else the file name. replace: an earlier book of the same title is replaced
    // (its analysis and its text are deleted) instead of refusing the new one.
    public async Task<AddBookResult> AddAsync(Stream? content, string? fileName, string? title, bool replace, CancellationToken cancellationToken)
    {
        var file = await ReadAllAsync(content, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return new AddBookResult(null, BookRejection.Empty);
        }

        if (file.Length > MaxBookBytes)
        {
            return new AddBookResult(null, BookRejection.TooLarge);
        }

        BookText read;
        try
        {
            // Reading a big PDF takes the processor for a while: not on the thread that serves the request.
            read = await Task.Run(() => extractor.Extract(file), cancellationToken).ConfigureAwait(false);
        }
        catch (BookReadException exception)
        {
            return new AddBookResult(null, exception.Rejection);
        }

        var safeName = CleanFileName(fileName);
        var bookTitle = CleanTitle(title) ?? CleanTitle(read.Title) ?? CleanTitle(Path.GetFileNameWithoutExtension(safeName)) ?? UntitledBook;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = (await books.ListAsync(cancellationToken).ConfigureAwait(false)).Books
                .Where(book => SameTitle(book.Title, bookTitle))
                .ToList();
            if (existing.Count > 0 && !replace)
            {
                return new AddBookResult(null, BookRejection.Duplicate);
            }

            var now = clock.GetUtcNow();
            var book = new Book(BookId.New(), bookTitle, safeName, now);
            await texts.SaveAsync(book.Id, read.Text, cancellationToken).ConfigureAwait(false);
            try
            {
                await books.SaveAsync(book, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The text of a book that does not exist must not stay on the disk.
                await texts.DeleteAsync(book.Id, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            // The new book is saved first: if something fails now, the user has two books instead of none.
            foreach (var old in existing)
            {
                await DeleteCoreAsync(old.Id, cancellationToken).ConfigureAwait(false);
            }

            return new AddBookResult(book, null, Replaced: existing.Count > 0);
        }
        finally
        {
            _lock.Release();
        }
    }

    // False when there was no such book (its text, if any is left, is deleted anyway).
    public async Task<bool> DeleteAsync(BookId id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await DeleteCoreAsync(id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<bool> DeleteCoreAsync(BookId id, CancellationToken cancellationToken)
    {
        var existed = await books.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await texts.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return existed;
    }

    // At most MaxBookBytes + 1 bytes are read: enough to know the file is too large, without swallowing a huge upload.
    // Null for no file or an empty one.
    private static async Task<MemoryStream?> ReadAllAsync(Stream? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (memory.Length <= MaxBookBytes)
        {
            var wanted = (int)Math.Min(buffer.Length, MaxBookBytes + 1 - memory.Length);
            var read = await content.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
        }

        if (memory.Length == 0)
        {
            await memory.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        memory.Position = 0;
        return memory;
    }

    private static bool SameTitle(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    // Control characters become spaces and runs of spaces become one; a line break in a title would break a page.
    private static string Normalize(string? text)
    {
        var builder = new StringBuilder();
        var space = false;
        foreach (var character in text ?? string.Empty)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string? CleanTitle(string? title)
    {
        var cleaned = Normalize(title);
        return cleaned.Length == 0 ? null : CharacterLimits.Clip(cleaned, BookLimits.Title);
    }

    // Only the name: a browser may send a whole path ("C:\fakepath\book.epub"), and a path must never go further.
    // Control characters are removed.
    private static string CleanFileName(string? fileName)
    {
        var name = fileName ?? string.Empty;
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        name = new string([.. name.Where(character => !char.IsControl(character))]).Trim();
        return CharacterLimits.Clip(name, BookLimits.FileName);
    }
}
