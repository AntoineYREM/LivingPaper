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
    // The story of the whole book, scene by scene, from the scenes noted in every part (PAPER-53).
    public const string StorySummary = "analysis.storySummary";
    // Added to analysis.readPart when the passages of the book are kept: asks which passages each scene covers (PAPER-84).
    public const string PassageRanges = "analysis.passageRanges";
    // The plot of the book in a few hundred words, written from its story (PAPER-84).
    public const string PlotSummary = "analysis.plotSummary";
    public const string ChatSystem = "chat.system";
    // The section of the chat prompt that gives the story to the character; left out for a book that has no story.
    public const string ChatStory = "chat.story";
    public const string ConnectionTest = "llm.connectionTest";

    private static readonly Dictionary<string, string[]> Variables = new(StringComparer.Ordinal)
    {
        [ReadPart] = [PromptVariables.BookTitle, PromptVariables.PassageRanges],
        [ReadPartSimple] = [PromptVariables.BookTitle],
        [MergeCharacters] = [PromptVariables.BookTitle, PromptVariables.MaxCharacters],
        [CharacterProfile] = [PromptVariables.BookTitle, PromptVariables.CharacterName, PromptVariables.ShortDescription],
        [BookSummary] = [PromptVariables.BookTitle],
        [StorySummary] = [PromptVariables.BookTitle, PromptVariables.MaxWords],
        [PassageRanges] = [],
        [PlotSummary] = [PromptVariables.BookTitle, PromptVariables.MaxWords],
        [ChatSystem] = [PromptVariables.BookTitle, PromptVariables.CharacterName, PromptVariables.Profile, PromptVariables.Story],
        [ChatStory] = [PromptVariables.BookTitle, PromptVariables.CharacterName, PromptVariables.StorySummary],
        [ConnectionTest] = [],
    };

    public static IReadOnlyList<string> All { get; } = [.. Variables.Keys];

    public static bool IsKnown(string? key) => key is not null && Variables.ContainsKey(key);

    public static IReadOnlyList<string> VariablesOf(string key) =>
        key is not null && Variables.TryGetValue(key, out var variables)
            ? variables
            : throw new ArgumentException($"'{key}' is not a known prompt key.", nameof(key));
}
