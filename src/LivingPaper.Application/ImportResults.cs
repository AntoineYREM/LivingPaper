using LivingPaper.Domain;

namespace LivingPaper.Application;

// A file for the user to download: a name for it, and its content.
public sealed record ExportFile(string FileName, byte[] Content);

// The outcome of importing a file: the book that was added, or why it was refused. ConflictTitles: the titles of the books of the
// library that the file would replace (when it is refused as a Conflict). Replaced: books were replaced because the user said so.
public sealed record ImportBookResult(Book? Imported, ImportRejection? Rejection, string? Detail, IReadOnlyList<string> ConflictTitles, bool Replaced);
