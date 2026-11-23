using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// Reads the prompts from {dataDirectory}/prompts.json at every call, so an edit is used without restarting. Only the
// asked prompt is validated: a mistake in the analysis prompts does not stop the chat. There is no fallback to the
// default prompts: a file that cannot be used is an explicit error naming the file and the key.
// A file other than {dataDirectory}/prompts.json can be given (the evaluation tool tries another set of prompts without touching yours).
public sealed class FilePromptProvider(string dataDirectory, string? filePath = null) : IPromptProvider
{
    public const string FileName = "prompts.json";

    private string PromptsPath => filePath ?? System.IO.Path.Combine(dataDirectory, FileName);

    public async Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var allowed = PromptKeys.VariablesOf(key);

        var file = await PromptFile.ReadAsync(PromptsPath, FileName, cancellationToken).ConfigureAwait(false);
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

    // A short signature of the prompts that make the character speak (the chat prompt and its story section): two runs with the same
    // signature were made with the same prompts. Line endings do not count. Twelve hex digits are plenty to tell versions apart.
    public async Task<string> ChatFingerprintAsync(CancellationToken cancellationToken)
    {
        var file = await PromptFile.ReadAsync(PromptsPath, FileName, cancellationToken).ConfigureAwait(false);
        var text = new System.Text.StringBuilder();
        foreach (var key in new[] { PromptKeys.ChatSystem, PromptKeys.ChatStory })
        {
            var template = file.Prompts!.TryGetValue(key, out var entry) ? entry?.Template ?? string.Empty : string.Empty;
            text.Append(key).Append('\n').Append(template.ReplaceLineEndings("\n")).Append('\n');
        }

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(hash)[..12];
    }
}
