using System.Text;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The text of a book waiting for its analysis: {uploadsDirectory}/{id}.txt, in UTF-8. The name is the book's id (32
// hexadecimal characters), so it can never hold a path. Nothing here is ever logged, exported or put in a book's JSON.
public sealed class FileBookTextStore(string uploadsDirectory) : IBookTextStore
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task SaveAsync(BookId bookId, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        await AtomicFile.WriteAsync(PathOf(bookId), Utf8.GetBytes(text), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReadAsync(BookId bookId, CancellationToken cancellationToken)
    {
        var path = PathOf(bookId);
        return File.Exists(path)
            ? Utf8.GetString(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))
            : null;
    }

    public Task DeleteAsync(BookId bookId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathOf(bookId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathOf(BookId bookId) => Path.Combine(uploadsDirectory, bookId + ".txt");
}
