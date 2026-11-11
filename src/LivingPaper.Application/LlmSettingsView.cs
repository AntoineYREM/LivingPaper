namespace LivingPaper.Application;

// What the settings page needs to show, with no key. IsAnalysisEnabled and IsChatEnabled are false until a usable model
// is chosen, for instance after the chosen one was deleted: the pages that analyze books and chat then say so.
public sealed record LlmSettingsView(
    IReadOnlyList<LlmProviderSummary> Providers,
    string? AnalysisProviderId,
    string? ChatProviderId,
    bool IsAnalysisEnabled,
    bool IsChatEnabled);
