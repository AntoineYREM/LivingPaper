using System.Text.Json.Nodes;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// 0.3.0: the address of a book is the slug of its title (/books/cyrano-de-bergerac), no longer 32 random characters. At start, a
// book of the library that still has an old identifier is renamed: its file (data/books/{id}.json), the steps of its analysis
// ({id}.steps.json) and its waiting text (uploads/{id}.txt), and the id written in the file. It is safe to run again: a book that
// was done is skipped. Nothing is ever deleted except the old file, after the
// new one is written.
public static class BookAddressMigration
{
    private const string Extension = ".json";
    private const string StepsSuffix = ".steps.json";

    // The identifiers that changed, old then new. reservedIds are the addresses of the examples: a book must not take one.
    public static async Task<IReadOnlyList<(string From, string To)>> RunAsync(
        string booksDirectory, string uploadsDirectory, IEnumerable<string> reservedIds, CancellationToken cancellationToken)
    {
        var moved = new List<(string From, string To)>();
        if (!Directory.Exists(booksDirectory))
        {
            return moved;
        }

        var names = Directory.EnumerateFiles(booksDirectory, "*" + Extension)
            .Select(Path.GetFileName)
            .Where(name => !name!.EndsWith(StepsSuffix, StringComparison.Ordinal))
            .Select(name => name![..^Extension.Length])
            .ToList();
        var taken = new HashSet<string>(names, StringComparer.Ordinal);
        taken.UnionWith(reservedIds);

        var old = new List<(string Id, string Title, DateTimeOffset SentAt, JsonNode Document)>();
        foreach (var name in names)
        {
            if (!BookId.TryParse(name, out var id) || !id.IsOldIdentifier)
            {
                continue;
            }

            var read = await TryReadAsync(Path.Combine(booksDirectory, name + Extension), cancellationToken).ConfigureAwait(false);
            if (read is { } document)
            {
                old.Add((name, document.Title, document.SentAt, document.Node));
            }
        }

        // The oldest book gets the plain address when two titles give the same slug.
        foreach (var book in old.OrderBy(book => book.SentAt).ThenBy(book => book.Id, StringComparer.Ordinal))
        {
            var slug = BookId.FromTitle(book.Title, taken.Contains).ToString();
            taken.Add(slug);

            Move(Path.Combine(booksDirectory, book.Id + StepsSuffix), Path.Combine(booksDirectory, slug + StepsSuffix));
            Move(Path.Combine(uploadsDirectory, book.Id + ".txt"), Path.Combine(uploadsDirectory, slug + ".txt"));

            book.Document["id"] = slug;
            if ((int?)book.Document["schemaVersion"] is < BookFile.CurrentSchemaVersion)
            {
                book.Document["schemaVersion"] = BookFile.CurrentSchemaVersion;
            }

            await AtomicFile.WriteAsync(
                Path.Combine(booksDirectory, slug + Extension),
                System.Text.Encoding.UTF8.GetBytes(book.Document.ToJsonString(BookFile.JsonOptions)),
                cancellationToken).ConfigureAwait(false);
            await SharedFile.DeleteIfExistsAsync(Path.Combine(booksDirectory, book.Id + Extension), cancellationToken).ConfigureAwait(false);
            moved.Add((book.Id, slug));
        }

        return moved;
    }

    // A file that is not a book (damaged, too big) is left alone: the library lists it as unreadable, as before.
    private static async Task<(string Title, DateTimeOffset SentAt, JsonNode Node)?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > JsonBookRepository.MaxFileBytes)
            {
                return null;
            }

            var node = JsonNode.Parse(await SharedFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
            if (node is JsonObject && node["title"] is JsonValue title && title.TryGetValue<string>(out var text)
                && node["id"] is JsonValue id && id.TryGetValue<string>(out var written) && written == Path.GetFileNameWithoutExtension(path))
            {
                var sentAt = node["sentAt"] is JsonValue sent && sent.TryGetValue<DateTimeOffset>(out var date) ? date : DateTimeOffset.MinValue;
                return (text, sentAt, node);
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or FormatException or InvalidOperationException)
        {
            // Not readable: left where it is.
        }

        return null;
    }

    private static void Move(string from, string to)
    {
        if (File.Exists(from) && !File.Exists(to))
        {
            File.Move(from, to);
        }
    }
}
