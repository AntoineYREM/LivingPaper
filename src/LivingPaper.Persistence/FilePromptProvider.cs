using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// Reads the prompts from {dataDirectory}/prompts.json at every call, so an edit is used without restarting. Only the
// asked prompt is validated: a mistake in the analysis prompts does not stop the chat. There is no fallback to the
// default prompts: a file that cannot be used is an explicit error naming the file and the key.
public sealed class FilePromptProvider(string dataDirectory) : IPromptProvider
{
    public const string FileName = "prompts.json";

    public async Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var allowed = PromptKeys.VariablesOf(key);

        var file = await PromptFile.ReadAsync(Path.Combine(dataDirectory, FileName), FileName, cancellationToken).ConfigureAwait(false);
        if (!file.Prompts!.TryGetValue(key, out var entry) || entry is null)
        {
            throw new PromptFileException(FileName, key, "the prompt is missing from the file");
        }

        if (string.IsNullOrWhiteSpace(entry.Template))
        {
            throw new PromptFileException(FileName, key, "the prompt is empty");
        }

        var template = PromptTemplate.Parse(entry.Template);
        var unknown = template.FindUnknownVariables(allowed);
        if (unknown.Count > 0)
        {
            var usable = allowed.Count == 0 ? "none" : string.Join(", ", allowed.Select(name => "{" + name + "}"));
            throw new PromptFileException(
                FileName,
                key,
                $"unknown variable {string.Join(", ", unknown.Select(name => "{" + name + "}"))}; this prompt may use: {usable}");
        }

        return template.Render(variables);
    }
}
