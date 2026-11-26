using LivingPaper.Domain;

namespace LivingPaper.Application;

// A file for the user to download: a name for it, its content, and what was left out so that it can be imported.
public sealed record ExportFile(string FileName, byte[] Content, ExportOmission Omitted = ExportOmission.None);

// What the export of a book will give: whether it holds pieces of its text, and what will be left out.
public sealed record ExportDescription(bool HasPassages, ExportOmission Omitted);

// The outcome of importing a file: the book that was added, or why it was refused. ConflictTitles: the titles of the books of the
// library that the file would replace (when it is refused as a Conflict). Replaced: books were replaced because the user said so.
// PassagesDropped: the file held passages, which the index could not keep.
public sealed record ImportBookResult(
    Book? Imported, ImportRejection? Rejection, string? Detail, IReadOnlyList<string> ConflictTitles, bool Replaced, bool PassagesDropped = false);
