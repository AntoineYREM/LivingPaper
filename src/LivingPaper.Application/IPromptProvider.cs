namespace LivingPaper.Application;

// Where the prompts sent to the AI come from. No prompt is written in the code: they live in a file the user can read
// and edit, and are read again at every call, so a change is used without restarting the application.
public interface IPromptProvider
{
    // The prompt for a key (see PromptKeys) with its variables replaced.
    // Throws PromptFileException when the prompt is missing, empty, uses an unknown variable or the file is invalid:
    // never a silent fallback to a default text. Throws ArgumentException when the code itself asks for an unknown key
    // or does not give a value the prompt needs.
    Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken);
}
