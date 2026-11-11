namespace LivingPaper.Domain;

// The user's language models and which one is used for analyzing books and which one for chatting. The two uses can
// share a model or use different ones (a cheap one to read books, a better one to chat).
public sealed class LlmSettings
{
    private readonly List<LlmProvider> _providers = [];
    private readonly HashSet<string> _unreadableKeyProviderIds = new(StringComparer.Ordinal);

    public IReadOnlyList<LlmProvider> Providers => _providers.AsReadOnly();

    public string? AnalysisProviderId { get; private set; }

    public string? ChatProviderId { get; private set; }

    // Models whose key was stored but cannot be decrypted any more (the encryption keys of the application were lost, for
    // instance a volume restored without them). They have an empty key and cannot be used until they are added again.
    public IReadOnlyCollection<string> UnreadableKeyProviderIds => _unreadableKeyProviderIds;

    public LlmProvider? AnalysisProvider => Find(AnalysisProviderId);

    public LlmProvider? ChatProvider => Find(ChatProviderId);

    // Analysis and chat are disabled until a usable model is chosen, for instance after the chosen one was deleted.
    public bool IsAnalysisEnabled => IsUsable(AnalysisProviderId);

    public bool IsChatEnabled => IsUsable(ChatProviderId);

    // Rebuilds the settings from storage. Nothing is selected by itself; a selection of a model that does not exist is
    // dropped. Throws ArgumentException when two models share an id or a name, or when there are too many.
    public static LlmSettings Restore(
        IEnumerable<LlmProvider> providers,
        string? analysisProviderId,
        string? chatProviderId,
        IEnumerable<string>? unreadableKeyProviderIds = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var settings = new LlmSettings();
        foreach (var provider in providers)
        {
            if (settings._providers.Count >= LlmProviderLimits.MaxProviders)
            {
                throw new ArgumentException($"There are more than {LlmProviderLimits.MaxProviders} language models.", nameof(providers));
            }

            if (settings._providers.Any(existing => existing.Id == provider.Id))
            {
                throw new ArgumentException("Two language models have the same id.", nameof(providers));
            }

            if (settings._providers.Any(existing => string.Equals(existing.Name.Trim(), provider.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Two language models have the same name.", nameof(providers));
            }

            settings._providers.Add(provider);
        }

        settings.AnalysisProviderId = settings.Find(analysisProviderId)?.Id;
        settings.ChatProviderId = settings.Find(chatProviderId)?.Id;
        foreach (var id in unreadableKeyProviderIds ?? [])
        {
            if (settings.Find(id) is not null)
            {
                settings._unreadableKeyProviderIds.Add(id);
            }
        }

        return settings;
    }

    // The very first model is chosen for both uses: the application is usable at once. Later ones are not chosen by
    // themselves. Throws InvalidOperationException when the id is already used or there are too many models: the caller
    // checks the name and the limit with the validator first.
    public void Add(LlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Any(existing => existing.Id == provider.Id))
        {
            throw new InvalidOperationException("A language model with this id already exists.");
        }

        if (_providers.Count >= LlmProviderLimits.MaxProviders)
        {
            throw new InvalidOperationException($"There cannot be more than {LlmProviderLimits.MaxProviders} language models.");
        }

        var first = _providers.Count == 0;
        _providers.Add(provider);
        if (first)
        {
            AnalysisProviderId = provider.Id;
            ChatProviderId = provider.Id;
        }
    }

    // A deleted model that was chosen is unchosen. Nothing is chosen in its place: the user picks another one.
    public bool Remove(string id)
    {
        var provider = Find(id);
        if (provider is null)
        {
            return false;
        }

        _providers.Remove(provider);
        _unreadableKeyProviderIds.Remove(id);
        if (AnalysisProviderId == id)
        {
            AnalysisProviderId = null;
        }

        if (ChatProviderId == id)
        {
            ChatProviderId = null;
        }

        return true;
    }

    // null: no model, which disables analysis. Throws ArgumentException for a model that does not exist.
    public void SelectForAnalysis(string? id) => AnalysisProviderId = ResolveSelection(id);

    public void SelectForChat(string? id) => ChatProviderId = ResolveSelection(id);

    private string? ResolveSelection(string? id)
    {
        if (id is null)
        {
            return null;
        }

        return Find(id)?.Id ?? throw new ArgumentException("There is no such language model.", nameof(id));
    }

    private LlmProvider? Find(string? id) => id is null ? null : _providers.Find(provider => provider.Id == id);

    private bool IsUsable(string? id) => Find(id) is not null && !_unreadableKeyProviderIds.Contains(id!);
}
