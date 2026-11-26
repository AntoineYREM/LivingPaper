namespace LivingPaper.Application;

// What the settings page needs to show, with no key. IsAnalysisEnabled and IsChatEnabled are false until a usable model
// is chosen, for instance after the chosen one was deleted: the pages that analyze books and chat then say so.
// EmbeddingProviderId: the model for vectorization, which is optional.
public sealed record LlmSettingsView(
    IReadOnlyList<LlmProviderSummary> Providers,
    string? AnalysisProviderId,
    string? ChatProviderId,
    bool IsAnalysisEnabled,
    bool IsChatEnabled,
    string? EmbeddingProviderId = null)
{
    // A use with no model: the next model added is tried, to know whether it can fill it.
    public bool HasEmptyChoice => AnalysisProviderId is null || ChatProviderId is null || EmbeddingProviderId is null;
}
