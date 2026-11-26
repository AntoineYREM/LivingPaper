namespace LivingPaper.Application;

// The names of the variables a prompt may contain, written once so that the code asking for a prompt cannot misspell one.
public static class PromptVariables
{
    public const string BookTitle = "bookTitle";
    public const string CharacterName = "characterName";
    public const string ShortDescription = "shortDescription";
    public const string Profile = "profile";
    public const string MaxCharacters = "maxCharacters";

    // The number of words the story of the book may take (analysis.storySummary).
    public const string MaxWords = "maxWords";

    // The story of the book, scene by scene (chat.story), and the whole section of the chat prompt that holds it (chat.system).
    public const string StorySummary = "storySummary";
    public const string PassageRanges = "passageRanges";
    public const string Story = "story";
}
