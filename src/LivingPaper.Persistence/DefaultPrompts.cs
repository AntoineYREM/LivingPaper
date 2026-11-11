using System.Reflection;
using System.Text.Json;

namespace LivingPaper.Persistence;

// The prompts shipped with the application (prompts.default.json, embedded in this assembly).
internal static class DefaultPrompts
{
    private const string ResourceName = "prompts.default.json";

    public static PromptFile Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing from the build.");
        return JsonSerializer.Deserialize<PromptFile>(stream, PromptFile.JsonOptions)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is empty.");
    }
}
