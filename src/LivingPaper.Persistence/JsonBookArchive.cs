using System.Text.Encodings.Web;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The file of an exported book: the JSON of a book of the library (BookFile), readable (indented, letters with accents written
// as they are), with its passages when it has some. Reading one is reading untrusted input: the same validation as for a book
// read from the disk, with a reason for each kind of refusal.
// maxBytes: the largest file with its passages. maxBookBytes: the largest book, passages and vectors left aside.
public sealed class JsonBookArchive(long maxBytes = BookService.MaxImportBytes, int maxBookBytes = JsonBookRepository.MaxFileBytes) : IBookArchive
{
    // Not HTML: the file is downloaded, never put in a page, so accents need not be escaped.
    private static readonly JsonSerializerOptions ExportOptions = new(BookFile.JsonOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public byte[] Export(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return Serialize(BookFile.From(book));
    }

    public ArchivedBook ExportWithPassages(Book book, PassageSet passages)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(passages);
        var file = BookFile.From(book);
        if (passages.Passages.Count == 0)
        {
            return new ArchivedBook(Serialize(file), ExportOmission.None);
        }

        var withVectors = passages.EmbeddingModel is not null && passages.Vectors.Count == passages.Passages.Count;
        file.PassageIndex = PassageIndexFile.From(passages, withVectors);
        var content = Serialize(file);
        if (content.Length <= maxBytes)
        {
            return new ArchivedBook(content, ExportOmission.None);
        }

        if (withVectors)
        {
            file.PassageIndex = PassageIndexFile.From(passages, withVectors: false);
            content = Serialize(file);
            if (content.Length <= maxBytes)
            {
                return new ArchivedBook(content, ExportOmission.Vectors);
            }
        }

        file.PassageIndex = null;
        return new ArchivedBook(Serialize(file), ExportOmission.Passages);
    }

    public ArchiveParseResult Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return Refused(ImportRejection.Empty);
        }

        if (content.Length > maxBytes)
        {
            return Refused(ImportRejection.TooLarge);
        }

        BookFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BookFile>(content, BookFile.JsonOptions);
        }
        catch (JsonException)
        {
            return Refused(ImportRejection.NotJson);
        }

        if (file is null)
        {
            return Refused(ImportRejection.NotJson);
        }

        if (file.SchemaVersion > BookFile.CurrentSchemaVersion)
        {
            return Refused(ImportRejection.NewerVersion);
        }

        if (file.SchemaVersion < BookFile.OldestSchemaVersion)
        {
            return Refused(ImportRejection.UnknownVersion);
        }

        if (!BookFitsItsLimit(file, content.Length, maxBookBytes))
        {
            return Refused(ImportRejection.TooLarge);
        }

        if (!BookId.TryParse(file.Id, out var id))
        {
            return Refused(ImportRejection.Invalid, "id");
        }

        var book = file.ToBook(id, out var reason);
        if (book is null)
        {
            return Refused(ImportRejection.Invalid, reason);
        }

        if (file.PassageIndex is null)
        {
            return new ArchiveParseResult(book, null);
        }

        var passages = file.PassageIndex.ToSet(out var field);
        return passages is null ? Refused(ImportRejection.Invalid, field) : new ArchiveParseResult(book, null, Passages: passages);
    }

    // Whether the book of a file of "length" bytes, passages and vectors left aside, is no larger than a book may be: it
    // is then saved in the library, where a larger one could not be read back.
    internal static bool BookFitsItsLimit(BookFile file, long length, int limit)
    {
        if (file.PassageIndex is not { } passages)
        {
            return length <= limit;
        }

        file.PassageIndex = null;
        var size = Serialize(file).Length;
        file.PassageIndex = passages;
        return size <= limit;
    }

    private static byte[] Serialize(BookFile file) => JsonSerializer.SerializeToUtf8Bytes(file, ExportOptions);

    private static ArchiveParseResult Refused(ImportRejection rejection, string? detail = null) => new(null, rejection, detail);
}
