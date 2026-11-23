using System.Text.Json;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// Reads a questions file. It is checked like any file that comes from outside: its schema, its size and the length of its texts.
internal static partial class QuestionFile
{
    public const long MaxFileBytes = 1_048_576;
    public const int MaxItems = 500;

    private const int MaxIdLength = 80;
    private const int MaxCategoryLength = 40;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<QuestionSet> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new EvalException($"The questions file {name} does not exist.");
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new EvalException($"The questions file {name} is too large.");
        }

        FileContent? file;
        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync<FileContent>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new EvalException($"The questions file {name} is not valid JSON.");
        }

        if (file is null || file.SchemaVersion != 1)
        {
            throw new EvalException($"The questions file {name} must have \"schemaVersion\": 1.");
        }

        Require(!string.IsNullOrWhiteSpace(file.Dataset), name, "dataset");
        Require(BookId.TryParse(file.BookId, out _), name, "bookId (the address of an example book)");
        Require(!string.IsNullOrWhiteSpace(file.Character), name, "character");

        var items = file.Items ?? [];
        Require(items.Count is > 0 and <= MaxItems, name, $"items (1 to {MaxItems})");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var questions = new List<EvalQuestion>();
        foreach (var item in items)
        {
            Require(item?.Id is { Length: > 0 and <= MaxIdLength } id && IdPattern().IsMatch(id), name, "an item id (lowercase letters, digits and hyphens)");
            Require(seen.Add(item!.Id!), name, $"unique item ids ({item.Id} is repeated)");
            Require(!string.IsNullOrWhiteSpace(item.Category) && item.Category.Length <= MaxCategoryLength, name, $"the category of {item.Id}");
            Require(
                !string.IsNullOrWhiteSpace(item.Input) && item.Input.Length <= ConversationLimits.MaxMessageLength,
                name,
                $"the input of {item.Id} (1 to {ConversationLimits.MaxMessageLength} characters)");
            questions.Add(new EvalQuestion(item.Id!, item.Category!.Trim(), item.Input!.Trim(), item.ExpectedOutput?.Trim() ?? string.Empty));
        }

        return new QuestionSet(file.Dataset!.Trim(), file.BookId!, file.Character!.Trim(), questions);
    }

    private static void Require(bool condition, string fileName, string what)
    {
        if (!condition)
        {
            throw new EvalException($"The questions file {fileName} has no valid {what}.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex IdPattern();

    private sealed record FileContent(int SchemaVersion, string? Dataset, string? BookId, string? Character, List<ItemContent?>? Items);

    private sealed record ItemContent(string? Id, string? Category, string? Input, string? ExpectedOutput);
}
