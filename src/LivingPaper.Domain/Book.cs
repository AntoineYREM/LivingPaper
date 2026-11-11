namespace LivingPaper.Domain;

// A book sent to LivingPaper, with the result of its analysis. The text of the book itself is never kept.
public sealed class Book(BookId id, string title, string fileName, DateTimeOffset sentAt)
{
    public BookId Id { get; } = id;

    // The title read from the file, or the file name when it has none.
    public string Title { get; set; } = title;

    public string FileName { get; set; } = fileName;

    // The book's era, place and tone, written by the analysis. Null until the analysis has produced it.
    public string? Summary { get; set; }

    public BookStatus Status { get; set; } = BookStatus.Waiting;

    // Where the analysis is, as the page shows it: "part 3 of 8", then "profile 4 of 10".
    public int PartsDone { get; set; }

    public int PartsTotal { get; set; }

    public int ProfilesDone { get; set; }

    public int ProfilesTotal { get; set; }

    // Set when Status is Failed. ErrorCode is a stable key the interface translates; ErrorDetail is a short technical
    // text in English, never containing a secret or a passage of the book.
    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    // The provider and the model that ran the analysis (never its key).
    public string? ProviderName { get; set; }

    public string? Model { get; set; }

    public int InputTokens { get; set; }

    public int CachedInputTokens { get; set; }

    public int OutputTokens { get; set; }

    public DateTimeOffset SentAt { get; set; } = sentAt;

    // Moved at every step. A Waiting or Running book not updated for 10 minutes is shown as interrupted.
    public DateTimeOffset UpdatedAt { get; set; } = sentAt;

    public DateTimeOffset? FinishedAt { get; set; }

    public List<Character> Characters { get; } = [];

    // The fields whose text is longer than allowed, named as in the JSON file. Empty when the book is acceptable.
    public IReadOnlyList<string> FindLimitViolations()
    {
        var violations = new List<string>();
        AddIfTooLong(violations, "title", Title, BookLimits.Title);
        AddIfTooLong(violations, "fileName", FileName, BookLimits.FileName);
        AddIfTooLong(violations, "summary", Summary, CharacterLimits.Summary);
        AddIfTooLong(violations, "errorDetail", ErrorDetail, BookLimits.ErrorDetail);
        AddIfTooLong(violations, "providerName", ProviderName, BookLimits.ProviderName);
        AddIfTooLong(violations, "model", Model, BookLimits.ModelName);

        if (Characters.Count > BookLimits.MaxCharacters)
        {
            violations.Add("characters");
        }

        for (var index = 0; index < Characters.Count; index++)
        {
            var character = Characters[index];
            AddIfTooLong(violations, $"characters[{index}].name", character.Name, CharacterLimits.Name);
            AddIfTooLong(violations, $"characters[{index}].shortDescription", character.ShortDescription, CharacterLimits.ShortDescription);
            AddIfTooLong(violations, $"characters[{index}].chatProfile", character.ChatProfile, CharacterLimits.ChatProfile);
        }

        return violations;
    }

    private static void AddIfTooLong(List<string> violations, string field, string? text, int max)
    {
        if (text is not null && text.Length > max)
        {
            violations.Add(field);
        }
    }
}
