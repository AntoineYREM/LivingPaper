using System.Globalization;
using System.Text;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// The use cases of the books: sending, deleting, retrying the analysis of one, and finding again after a restart the books
// whose analysis was cut. A book is read at once (so a scanned PDF or a damaged file is refused in
// front of the user, not minutes later), created waiting for its analysis, and its text is kept apart until the analysis
// has finished.
// Sending is done one at a time (a lock around the check for the same title and the save), so two sends of the same title
// at once cannot both succeed.
public sealed class BookService(
    IBookRepository books,
    IBookTextStore texts,
    IBookTextExtractor extractor,
    IAnalysisScheduler scheduler,
    TimeProvider clock,
    AnalysisOptions options,
    IExampleCatalog? examples = null,
    IBookArchive? archive = null) : IDisposable
{
    // The largest book file accepted: 20 MB.
    public const long MaxBookBytes = 20L * 1024 * 1024;

    // The largest file of an exported book that is accepted for import: 1 MB, as large as a book file read from the disk.
    public const long MaxImportBytes = 1_048_576;

    // The title of a book whose file and sender give none.
    public const string UntitledBook = "Untitled book";

    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task<BookListing> ListAsync(CancellationToken cancellationToken) => books.ListAsync(cancellationToken);

    // content: the file as sent, which need not be seekable; at most MaxBookBytes are read. title: the title the user
    // typed, if any; else the one of the file; else the file name. replace: an earlier book of the same title is replaced
    // (its analysis and its text are deleted) instead of refusing the new one.
    public async Task<AddBookResult> AddAsync(Stream? content, string? fileName, string? title, bool replace, CancellationToken cancellationToken)
    {
        var file = await ReadAllAsync(content, MaxBookBytes, cancellationToken).ConfigureAwait(false);
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

            // The analysis starts by itself: there is no confirmation step.
            scheduler.Enqueue(book.Id);
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

    // A book of the library, or else an example, by id: what the pages that show a book use. The library comes first. Changing
    // a sheet, deleting and retrying only ever look in the library, so an example is read only whatever a page does.
    public async Task<BookLookup?> FindAsync(BookId id, CancellationToken cancellationToken)
    {
        var book = await books.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (book is not null)
        {
            return new BookLookup(book, IsExample: false);
        }

        var example = examples is null ? null : await examples.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return example is null ? null : new BookLookup(example, IsExample: true);
    }

    public async Task<BookListing> ListExamplesAsync(CancellationToken cancellationToken) =>
        examples is null ? new BookListing([], []) : await examples.ListAsync(cancellationToken).ConfigureAwait(false);

    // Copies an example into the library: a book that is done, with its sheets (which can then be edited), under a new id.
    // Refused when a book of the library has the same title: nothing is replaced behind the user's back.
    public async Task<AddExampleResult> AddExampleAsync(BookId exampleId, CancellationToken cancellationToken)
    {
        var example = examples is null ? null : await examples.GetAsync(exampleId, cancellationToken).ConfigureAwait(false);
        if (example is null)
        {
            return new AddExampleResult(null, ExampleRejection.NotFound);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inLibrary = (await books.ListAsync(cancellationToken).ConfigureAwait(false)).Books;
            if (inLibrary.Any(book => SameTitle(book.Title, example.Title)))
            {
                return new AddExampleResult(null, ExampleRejection.Duplicate);
            }

            var now = clock.GetUtcNow();
            var copy = new Book(BookId.New(), example.Title, example.FileName, now)
            {
                Summary = example.Summary,
                Status = BookStatus.Done,
                PartsDone = example.PartsDone,
                PartsTotal = example.PartsTotal,
                ProfilesDone = example.ProfilesDone,
                ProfilesTotal = example.ProfilesTotal,
                PartsSkipped = example.PartsSkipped,
                ProviderName = example.ProviderName,
                Model = example.Model,
                InputTokens = example.InputTokens,
                CachedInputTokens = example.CachedInputTokens,
                OutputTokens = example.OutputTokens,
                StartedAt = example.StartedAt,
                FinishedAt = example.FinishedAt,
                UpdatedAt = now,
            };
            copy.Characters.AddRange(example.Characters);
            await books.SaveAsync(copy, cancellationToken).ConfigureAwait(false);
            return new AddExampleResult(copy, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    // The file of an analyzed book, from the library or among the examples, for the user to download. Null when there is no such
    // book or its analysis is not done (there is nothing worth exporting yet). The file is built by the archive from the fields of
    // the book: it holds nothing else.
    public async Task<ExportFile?> ExportAsync(BookId id, CancellationToken cancellationToken)
    {
        var format = archive ?? throw new InvalidOperationException("No archive is configured: books cannot be exported.");
        var found = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        return found is { Book.Status: BookStatus.Done } ? new ExportFile(FileNameOf(found.Book.Title), format.Export(found.Book)) : null;
    }

    // Adds the book of an exported file to the library. The file is untrusted input: it is read at most MaxImportBytes, validated by
    // the archive (schema, sizes, lengths), then by the rules of a book (analyzed, sheets that follow the rules of a sheet, texts
    // without control characters), before anything is kept. A book of the library with the same identifier or the same title is
    // not replaced unless replace is true: the user confirms. The identifier of the file is kept when it is free, so importing a
    // file again updates the same book; one that an example already has gets a new identifier, so the example is not hidden.
    public async Task<ImportBookResult> ImportAsync(Stream? content, bool replace, CancellationToken cancellationToken)
    {
        var format = archive ?? throw new InvalidOperationException("No archive is configured: books cannot be imported.");
        var file = await ReadAllAsync(content, MaxImportBytes, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return ImportRefused(ImportRejection.Empty);
        }

        if (file.Length > MaxImportBytes)
        {
            return ImportRefused(ImportRejection.TooLarge);
        }

        var parsed = format.Parse(file.ToArray());
        if (parsed.Book is null)
        {
            return ImportRefused(parsed.Rejection ?? ImportRejection.Invalid, parsed.Detail);
        }

        if (CheckImported(parsed.Book) is var (rejection, detail))
        {
            return ImportRefused(rejection, detail);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inLibrary = (await books.ListAsync(cancellationToken).ConfigureAwait(false)).Books;
            var conflicts = inLibrary.Where(book => book.Id == parsed.Book.Id || SameTitle(book.Title, parsed.Book.Title)).ToList();
            if (conflicts.Count > 0 && !replace)
            {
                return new ImportBookResult(null, ImportRejection.Conflict, null, [.. conflicts.Select(book => book.Title)], Replaced: false);
            }

            var isExampleId = examples is not null && await examples.GetAsync(parsed.Book.Id, cancellationToken).ConfigureAwait(false) is not null;
            var imported = CleanedCopy(parsed.Book, isExampleId ? BookId.New() : parsed.Book.Id);
            foreach (var conflict in conflicts)
            {
                await DeleteCoreAsync(conflict.Id, cancellationToken).ConfigureAwait(false);
            }

            await books.SaveAsync(imported, cancellationToken).ConfigureAwait(false);
            return new ImportBookResult(imported, null, null, [], Replaced: conflicts.Count > 0);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static ImportBookResult ImportRefused(ImportRejection rejection, string? detail = null) =>
        new(null, rejection, detail, [], Replaced: false);

    // The rules of an analyzed book that the archive does not check. The detail names the field, never what was written in it.
    private static (ImportRejection Rejection, string? Detail)? CheckImported(Book book)
    {
        if (book.Status != BookStatus.Done || book.Characters.Count == 0)
        {
            return (ImportRejection.NotAnalyzed, null);
        }

        if (Normalize(book.Title).Length == 0 || HasControlCharacter(book.Title))
        {
            return (ImportRejection.Invalid, "title");
        }

        foreach (var (field, text) in new[] { ("fileName", book.FileName), ("providerName", book.ProviderName ?? string.Empty), ("model", book.Model ?? string.Empty) })
        {
            if (HasControlCharacter(text))
            {
                return (ImportRejection.Invalid, field);
            }
        }

        if ((book.Summary ?? string.Empty).Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
        {
            return (ImportRejection.Invalid, "summary");
        }

        if (book.Characters.Select(character => character.Position).Distinct().Count() != book.Characters.Count)
        {
            return (ImportRejection.Invalid, "characters: two have the same position");
        }

        for (var index = 0; index < book.Characters.Count; index++)
        {
            var character = book.Characters[index];
            var others = book.Characters.Where((_, other) => other != index).Select(other => other.Name);
            if (CharacterValidator.Validate(new CharacterDraft(character.Name, character.ShortDescription, character.ChatProfile), others) is [var first, ..])
            {
                return (ImportRejection.Invalid, $"characters[{index}].{first.Field}: {first.Code}");
            }
        }

        return null;
    }

    private static bool HasControlCharacter(string text) => text.Any(char.IsControl);

    // The book as it is kept: the texts cleaned like those of a sheet that is saved, the name of the file without any folder.
    private static Book CleanedCopy(Book source, BookId id)
    {
        var copy = new Book(id, source.Title.Trim(), CleanFileName(source.FileName), source.SentAt)
        {
            Summary = source.Summary,
            Status = BookStatus.Done,
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
        copy.Characters.AddRange(source.Characters.Select(character =>
        {
            var cleaned = new CharacterDraft(character.Name, character.ShortDescription, character.ChatProfile).Normalized();
            return new Character(character.Position, cleaned.Name!, cleaned.ShortDescription!, cleaned.ChatProfile!);
        }));
        return copy;
    }

    // A plain ASCII name for the downloaded file: letters and digits of the title, accents removed, anything else a hyphen. Never
    // a path, a quote or a line break, whatever the title holds.
    private static string FileNameOf(string title)
    {
        var slug = new StringBuilder();
        foreach (var character in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(lower);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var name = slug.ToString().Trim('-');
        name = name.Length > 60 ? name[..60].Trim('-') : name;
        return (name.Length == 0 ? "book" : name) + ".json";
    }

    // A book as it is stored, or null when there is none with this id. Throws BookUnreadableException when it cannot be read.
    public Task<Book?> GetAsync(BookId id, CancellationToken cancellationToken) => books.GetAsync(id, cancellationToken);

    // Changes the sheet of one character (position 1 is the most important) of a book whose analysis is done. The chat always
    // uses the texts saved here. Done one change at a time, so two changes at once cannot overwrite each other.
    public async Task<UpdateCharacterResult> UpdateCharacterAsync(BookId id, int position, CharacterDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var book = await books.GetAsync(id, cancellationToken).ConfigureAwait(false);
            var index = book?.Characters.FindIndex(character => character.Position == position) ?? -1;
            if (book is null || index < 0)
            {
                return new UpdateCharacterResult(CharacterUpdateOutcome.NotFound, [], null);
            }

            if (book.Status != BookStatus.Done)
            {
                return new UpdateCharacterResult(CharacterUpdateOutcome.NotEditable, [], null);
            }

            var errors = CharacterValidator.Validate(draft, book.Characters.Where(character => character.Position != position).Select(character => character.Name));
            if (errors.Count > 0)
            {
                return new UpdateCharacterResult(CharacterUpdateOutcome.Invalid, errors, null);
            }

            var normalized = draft.Normalized();
            var changed = book.Characters[index] with { Name = normalized.Name!, ShortDescription = normalized.ShortDescription!, ChatProfile = normalized.ChatProfile! };
            book.Characters[index] = changed;
            await books.SaveAsync(book, cancellationToken).ConfigureAwait(false);
            return new UpdateCharacterResult(CharacterUpdateOutcome.Saved, [], changed);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Puts a book whose analysis failed or was interrupted back in the queue: it resumes at the step that failed, the steps
    // already done are not paid for again. False when there is nothing to retry: no such book, a book that is done or
    // really waiting or running, or a book whose text is gone (it must be sent again).
    public async Task<bool> RetryAsync(BookId id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var book = await books.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (book is null || !(book.Status == BookStatus.Failed || IsInterrupted(book))
                || await texts.ReadAsync(id, cancellationToken).ConfigureAwait(false) is null)
            {
                return false;
            }

            // A stuck analysis is stopped first, so it cannot write over the new one.
            await scheduler.CancelAsync(id, cancellationToken).ConfigureAwait(false);
            book.Status = BookStatus.Waiting;
            book.ErrorCode = null;
            book.ErrorDetail = null;
            book.FinishedAt = null;
            book.UpdatedAt = clock.GetUtcNow();
            await books.SaveAsync(book, cancellationToken).ConfigureAwait(false);
            scheduler.Enqueue(id);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    // A book that says it is waiting or running but is not in the queue of this application was cut by a restart; a
    // running one that has not moved for ten minutes is stuck. Either way it can be retried.
    public bool IsInterrupted(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.Status switch
        {
            BookStatus.Waiting => !scheduler.IsQueued(book.Id),
            BookStatus.Running => !scheduler.IsQueued(book.Id) || clock.GetUtcNow() - book.UpdatedAt > options.InterruptedAfter,
            _ => false,
        };
    }

    // At startup: a book "running" belongs to an application that is gone, so it is interrupted (and can be retried); the
    // books "waiting" are queued again, oldest first, so a restart loses nothing.
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var listed = (await books.ListAsync(cancellationToken).ConfigureAwait(false)).Books;
            foreach (var book in listed.Where(book => book.Status == BookStatus.Running))
            {
                book.Status = BookStatus.Failed;
                book.ErrorCode = nameof(AnalysisFailure.Interrupted);
                book.ErrorDetail = "The application stopped while the book was being analyzed.";
                book.UpdatedAt = clock.GetUtcNow();
                await books.SaveAsync(book, cancellationToken).ConfigureAwait(false);
            }

            foreach (var book in listed.Where(book => book.Status == BookStatus.Waiting).OrderBy(book => book.SentAt))
            {
                scheduler.Enqueue(book.Id);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<bool> DeleteCoreAsync(BookId id, CancellationToken cancellationToken)
    {
        // The analysis of the book, if one is running, is stopped and awaited first: it would write the book again.
        await scheduler.CancelAsync(id, cancellationToken).ConfigureAwait(false);
        var existed = await books.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await texts.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return existed;
    }

    // At most limit + 1 bytes are read: enough to know the file is too large, without swallowing a huge upload.
    // Null for no file or an empty one.
    private static async Task<MemoryStream?> ReadAllAsync(Stream? content, long limit, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (memory.Length <= limit)
        {
            var wanted = (int)Math.Min(buffer.Length, limit + 1 - memory.Length);
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
