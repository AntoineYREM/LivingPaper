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

    // The story of the book, scene by scene, with who is present in each: what a character knows of the plot when it chats.
    // Written by the analysis, in English. Null for a book analyzed before it existed, or when no scene could be read.
    public string? StorySummary { get; set; }

    // True when some parts of the book gave no scenes (the model's content filter refused them, or only simple notes were
    // possible): the story then has holes.
    public bool StorySummaryPartial { get; set; }

    // The plot of the book in a few hundred words, ending included, in English: given to a character that chats with passages
    // of the book. Null for a book analyzed before it existed, with no story, or when the model refused to write it.
    public string? PlotSummary { get; set; }

    public BookStatus Status { get; set; } = BookStatus.Waiting;

    // Where the analysis is, as the page shows it: "part 3 of 8", then "profile 4 of 10".
    public int PartsDone { get; set; }

    public int PartsTotal { get; set; }

    public int ProfilesDone { get; set; }

    public int ProfilesTotal { get; set; }

    // While the story of the book is being written: which try it is at (1, 2, ...) out of how many at most. Both are 0 when it is
    // not being written. The first try is the normal one; the others are made because the story was too long.
    public int StoryTry { get; set; }

    public int StoryTries { get; set; }

    // Set when Status is Failed. ErrorCode is a stable key the interface translates; ErrorDetail is a short technical
    // text in English, never containing a secret or a passage of the book.
    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    // The provider and the model that ran the analysis (never its key).
    public string? ProviderName { get; set; }

    public string? Model { get; set; }

    // How many parts of the book the model's content filter refused even with simple notes: they were left out of the
    // analysis, and the page says so.
    public int PartsSkipped { get; set; }

    public int InputTokens { get; set; }

    public int CachedInputTokens { get; set; }

    public int OutputTokens { get; set; }

    // The tokens of the vectors of the passages, when the provider reports them.
    public int EmbeddingTokens { get; set; }

    // While the passages are turned into vectors: how many are done, out of how many. Both 0 the rest of the time.
    public int IndexDone { get; set; }

    public int IndexTotal { get; set; }

    // Why the passages have no vectors, as a stable key the interface translates. Null when there is nothing to report.
    public string? IndexErrorCode { get; set; }

    public DateTimeOffset SentAt { get; set; } = sentAt;

    // When the analysis last started (a retry starts it again). With FinishedAt it gives the duration of the analysis.
    public DateTimeOffset? StartedAt { get; set; }

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
        AddIfTooLong(violations, "storySummary", StorySummary, CharacterLimits.StorySummary);
        AddIfTooLong(violations, "plotSummary", PlotSummary, CharacterLimits.PlotSummary);
        AddIfTooLong(violations, "errorDetail", ErrorDetail, BookLimits.ErrorDetail);
        AddIfTooLong(violations, "indexErrorCode", IndexErrorCode, BookLimits.ErrorDetail);
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
