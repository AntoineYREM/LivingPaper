using System.Text.Encodings.Web;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The file of an exported book: the JSON of a book of the library (BookFile), readable (indented, letters with accents written
// as they are). Reading one is reading untrusted input: the same validation as for a book read from the disk, with a reason for
// each kind of refusal.
public sealed class JsonBookArchive : IBookArchive
{
    // Not HTML: the file is downloaded, never put in a page, so accents need not be escaped.
    private static readonly JsonSerializerOptions ExportOptions = new(BookFile.JsonOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public byte[] Export(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return JsonSerializer.SerializeToUtf8Bytes(BookFile.From(book), ExportOptions);
    }

    public ArchiveParseResult Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return Refused(ImportRejection.Empty);
        }

        if (content.Length > JsonBookRepository.MaxFileBytes)
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

        if (file.SchemaVersion != BookFile.CurrentSchemaVersion)
        {
            return Refused(ImportRejection.UnknownVersion);
        }

        if (!BookId.TryParse(file.Id, out var id))
        {
            return Refused(ImportRejection.Invalid, "id");
        }

        var book = file.ToBook(id, out var reason);
        return book is null ? Refused(ImportRejection.Invalid, reason) : new ArchiveParseResult(book, null);
    }

    private static ArchiveParseResult Refused(ImportRejection rejection, string? detail = null) => new(null, rejection, detail);
}
