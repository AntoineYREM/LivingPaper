using System.Text;

namespace LivingPaper.Domain;

// What the user typed to add a language model, before it is validated. Any field can be missing.
// Like LlmProvider, its ToString prints neither the key nor the base URL (which may carry a password).
public sealed record LlmProviderDraft(string? Name, string? BaseUrl, string? ApiKey, string? Model)
{
    // The same fields with the spaces and line breaks around them removed (a key pasted with a trailing newline is
    // common) and missing fields as empty text.
    public LlmProviderDraft Normalized() => new(
        Name?.Trim() ?? string.Empty,
        BaseUrl?.Trim() ?? string.Empty,
        ApiKey?.Trim() ?? string.Empty,
        Model?.Trim() ?? string.Empty);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Name = ").Append(Name).Append(", Model = ").Append(Model);
        return true;
    }
}
