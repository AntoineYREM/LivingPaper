using System.Text.Json;
using System.Text.Json.Serialization;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The shape of data/books/{id}.json. Kept apart from the Domain so the file format can evolve (schemaVersion)
// without changing the business model. Everything read from a file is untrusted: ToBook() validates it.
internal sealed class BookFile
{
    public const int CurrentSchemaVersion = 1;

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

    public string? Status { get; set; }

    public int PartsDone { get; set; }

    public int PartsTotal { get; set; }

    public int ProfilesDone { get; set; }

    public int ProfilesTotal { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    public string? ProviderName { get; set; }

    public string? Model { get; set; }

    public int InputTokens { get; set; }

    public int CachedInputTokens { get; set; }

    public int OutputTokens { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public List<CharacterFile>? Characters { get; set; }

    public static BookFile From(Book book) => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Id = book.Id.ToString(),
        Title = book.Title,
        FileName = book.FileName,
        Summary = book.Summary,
        Status = book.Status.ToString(),
        PartsDone = book.PartsDone,
        PartsTotal = book.PartsTotal,
        ProfilesDone = book.ProfilesDone,
        ProfilesTotal = book.ProfilesTotal,
        ErrorCode = book.ErrorCode,
        ErrorDetail = book.ErrorDetail,
        ProviderName = book.ProviderName,
        Model = book.Model,
        InputTokens = book.InputTokens,
        CachedInputTokens = book.CachedInputTokens,
        OutputTokens = book.OutputTokens,
        SentAt = book.SentAt,
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
        if (SchemaVersion != CurrentSchemaVersion)
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

        if (new[] { PartsDone, PartsTotal, ProfilesDone, ProfilesTotal, InputTokens, CachedInputTokens, OutputTokens }.Any(value => value < 0)
            || Characters.Any(character => character is null || character.Position < 1))
        {
            reason = "a number is out of range";
            return null;
        }

        var book = new Book(id, Title, FileName, SentAt.Value)
        {
            Summary = Summary,
            Status = Enum.Parse<BookStatus>(Status!),
            PartsDone = PartsDone,
            PartsTotal = PartsTotal,
            ProfilesDone = ProfilesDone,
            ProfilesTotal = ProfilesTotal,
            ErrorCode = ErrorCode,
            ErrorDetail = ErrorDetail,
            ProviderName = ProviderName,
            Model = Model,
            InputTokens = InputTokens,
            CachedInputTokens = CachedInputTokens,
            OutputTokens = OutputTokens,
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
