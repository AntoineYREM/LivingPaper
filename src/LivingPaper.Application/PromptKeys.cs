namespace LivingPaper.Application;

// The prompts of the application and the variables each one may use. This is the contract between the code, which
// provides the values, and data/prompts.json, which users may edit: a variable not listed here is refused.
public static class PromptKeys
{
    public const string ReadPart = "analysis.readPart";
    // The notes of a part read again when the model's content filter refused the rich notes: only names, roles and importance.
    public const string ReadPartSimple = "analysis.readPartSimple";
    public const string MergeCharacters = "analysis.mergeCharacters";
    public const string CharacterProfile = "analysis.characterProfile";
    public const string BookSummary = "analysis.bookSummary";
    public const string ChatSystem = "chat.system";
    public const string ConnectionTest = "llm.connectionTest";

    private static readonly Dictionary<string, string[]> Variables = new(StringComparer.Ordinal)
    {
        [ReadPart] = [PromptVariables.BookTitle],
        [ReadPartSimple] = [PromptVariables.BookTitle],
        [MergeCharacters] = [PromptVariables.BookTitle, PromptVariables.MaxCharacters],
        [CharacterProfile] = [PromptVariables.BookTitle, PromptVariables.CharacterName, PromptVariables.ShortDescription],
        [BookSummary] = [PromptVariables.BookTitle],
        [ChatSystem] = [PromptVariables.BookTitle, PromptVariables.CharacterName, PromptVariables.Profile],
        [ConnectionTest] = [],
    };

    public static IReadOnlyList<string> All { get; } = [.. Variables.Keys];

    public static bool IsKnown(string? key) => key is not null && Variables.ContainsKey(key);

    public static IReadOnlyList<string> VariablesOf(string key) =>
        key is not null && Variables.TryGetValue(key, out var variables)
            ? variables
            : throw new ArgumentException($"'{key}' is not a known prompt key.", nameof(key));
}
