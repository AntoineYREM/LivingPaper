using System.Text.Json;
using System.Text.Json.Serialization;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The shape of data/books/{id}.json. Kept apart from the Domain so the file format can evolve (schemaVersion)
// without changing the business model. Everything read from a file is untrusted: ToBook() validates it.
internal sealed class BookFile
{
    // 2 added the story of the book (storySummary). A file of version 1 has none and is still read.
    // 3: the id is a slug of the title (cyrano-de-bergerac) instead of 32 hexadecimal characters. Files of version 1 and 2 are still read.
    // 4 added the short summary of the plot (plotSummary), and the passages of the book in the file of an export or of an
    // example (passageIndex). Files of versions 1 to 3 have none and are still read.
    public const int CurrentSchemaVersion = 4;

    public const int OldestSchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public int SchemaVersion { get; set; }

    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? FileName { get; set; }

    public string? Summary { get; set; }

    public string? StorySummary { get; set; }

    // Written only when true (null otherwise), so a book without holes carries no extra field.
    public bool? StorySummaryPartial { get; set; }

    public string? PlotSummary { get; set; }

    public string? Status { get; set; }

    public int PartsDone { get; set; }

    public int PartsTotal { get; set; }

    public int ProfilesDone { get; set; }

    public int ProfilesTotal { get; set; }

    public int? StoryTry { get; set; }

    public int? StoryTries { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    public string? ProviderName { get; set; }

    public string? Model { get; set; }

    public int PartsSkipped { get; set; }

    public int InputTokens { get; set; }

    public int CachedInputTokens { get; set; }

    public int OutputTokens { get; set; }

    // Written only when there is something to say.
    public int? EmbeddingTokens { get; set; }

    public int? IndexDone { get; set; }

    public int? IndexTotal { get; set; }

    public string? IndexErrorCode { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public List<CharacterFile>? Characters { get; set; }

    // Only in the file of an export or of an example: the books of the library keep their passages in data/rag.db.
    public PassageIndexFile? PassageIndex { get; set; }

    public static BookFile From(Book book) => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Id = book.Id.ToString(),
        Title = book.Title,
        FileName = book.FileName,
        Summary = book.Summary,
        StorySummary = book.StorySummary,
        StorySummaryPartial = book.StorySummaryPartial ? true : null,
        PlotSummary = book.PlotSummary,
        Status = book.Status.ToString(),
        PartsDone = book.PartsDone,
        PartsTotal = book.PartsTotal,
        ProfilesDone = book.ProfilesDone,
        ProfilesTotal = book.ProfilesTotal,
        StoryTry = book.StoryTry > 0 ? book.StoryTry : null,
        StoryTries = book.StoryTries > 0 ? book.StoryTries : null,
        ErrorCode = book.ErrorCode,
        ErrorDetail = book.ErrorDetail,
        ProviderName = book.ProviderName,
        Model = book.Model,
        PartsSkipped = book.PartsSkipped,
        InputTokens = book.InputTokens,
        CachedInputTokens = book.CachedInputTokens,
        OutputTokens = book.OutputTokens,
        EmbeddingTokens = book.EmbeddingTokens > 0 ? book.EmbeddingTokens : null,
        IndexDone = book.IndexDone > 0 ? book.IndexDone : null,
        IndexTotal = book.IndexTotal > 0 ? book.IndexTotal : null,
        IndexErrorCode = book.IndexErrorCode,
        SentAt = book.SentAt,
        StartedAt = book.StartedAt,
        UpdatedAt = book.UpdatedAt,
        FinishedAt = book.FinishedAt,
        Characters = [.. book.Characters.Select(character => new CharacterFile
        {
            Position = character.Position,
            Name = character.Name,
            ShortDescription = character.ShortDescription,
            ChatProfile = character.ChatProfile,
        })],
    };

    // Returns the book, or null with the reason why this file is not an acceptable book. The id found in the file must
    // be the one in its name: a copied or renamed file is not trusted.
    public Book? ToBook(BookId expectedId, out string reason)
    {
        reason = string.Empty;
        if (SchemaVersion is < OldestSchemaVersion or > CurrentSchemaVersion)
        {
            reason = $"unsupported schemaVersion {SchemaVersion}";
            return null;
        }

        if (!BookId.TryParse(Id, out var id) || id != expectedId)
        {
            reason = "the id in the file does not match its name";
            return null;
        }

        if (Title is null || FileName is null || SentAt is null || UpdatedAt is null || Characters is null)
        {
            reason = "a required field is missing";
            return null;
        }

        if (!Enum.GetNames<BookStatus>().Contains(Status))
        {
            reason = "unknown status";
            return null;
        }

        if (new[] { PartsDone, PartsTotal, PartsSkipped, ProfilesDone, ProfilesTotal, StoryTry ?? 0, StoryTries ?? 0, InputTokens, CachedInputTokens, OutputTokens, EmbeddingTokens ?? 0, IndexDone ?? 0, IndexTotal ?? 0 }.Any(value => value < 0)
            || Characters.Any(character => character is null || character.Position < 1))
        {
            reason = "a number is out of range";
            return null;
        }

        var book = new Book(id, Title, FileName, SentAt.Value)
        {
            Summary = Summary,
            StorySummary = StorySummary,
            StorySummaryPartial = StorySummaryPartial == true,
            PlotSummary = PlotSummary,
            Status = Enum.Parse<BookStatus>(Status!),
            PartsDone = PartsDone,
            PartsTotal = PartsTotal,
            ProfilesDone = ProfilesDone,
            ProfilesTotal = ProfilesTotal,
            StoryTry = StoryTry ?? 0,
            StoryTries = StoryTries ?? 0,
            ErrorCode = ErrorCode,
            ErrorDetail = ErrorDetail,
            ProviderName = ProviderName,
            Model = Model,
            PartsSkipped = PartsSkipped,
            InputTokens = InputTokens,
            CachedInputTokens = CachedInputTokens,
            OutputTokens = OutputTokens,
            EmbeddingTokens = EmbeddingTokens ?? 0,
            IndexDone = IndexDone ?? 0,
            IndexTotal = IndexTotal ?? 0,
            IndexErrorCode = IndexErrorCode,
            StartedAt = StartedAt,
            UpdatedAt = UpdatedAt.Value,
            FinishedAt = FinishedAt,
        };
        book.Characters.AddRange(Characters.Select(character =>
            new Character(character.Position, character.Name ?? string.Empty, character.ShortDescription ?? string.Empty, character.ChatProfile ?? string.Empty)));

        var violations = book.FindLimitViolations();
        if (violations.Count > 0)
        {
            reason = $"text longer than allowed: {string.Join(", ", violations)}";
            return null;
        }

        return book;
    }
}

internal sealed class CharacterFile
{
    public int Position { get; set; }

    public string? Name { get; set; }

    public string? ShortDescription { get; set; }

    public string? ChatProfile { get; set; }
}
