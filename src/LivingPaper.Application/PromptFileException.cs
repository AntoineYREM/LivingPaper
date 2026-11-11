namespace LivingPaper.Application;

// The prompts file cannot be used. The message names the file and the key and says what is wrong; it never contains
// the text of a prompt value, a book or a chat message, so it can be logged and shown.
public sealed class PromptFileException(string fileName, string? key, string problem)
    : Exception(key is null ? $"{fileName}: {problem}" : $"{fileName}, prompt '{key}': {problem}")
{
    public string FileName { get; } = fileName;

    public string? Key { get; } = key;

    public string Problem { get; } = problem;
}
