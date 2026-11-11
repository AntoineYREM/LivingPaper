using LivingPaper.Domain;

namespace LivingPaper.Application;

// The use cases of the settings page: list the language models, add one (only after a successful connection test),
// delete one, and choose the one for analyzing books and the one for chatting.
// Changes are made one at a time (a lock around load, change, save) so two requests never overwrite each other. The
// connection test, which can take seconds, is made outside the lock.
// A key is only ever returned by GetAnalysisProviderAsync and GetChatProviderAsync, for the server to call the model.
public sealed class LlmSettingsService(ILlmSettingsRepository repository, ILlmConnectionTester tester) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<LlmSettingsView> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new LlmSettingsView(
            [.. settings.Providers.Select(provider => Summarize(provider, settings))],
            settings.AnalysisProviderId,
            settings.ChatProviderId,
            settings.IsAnalysisEnabled,
            settings.IsChatEnabled);
    }

    public async Task<AddProviderResult> AddAsync(LlmProviderDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var normalized = draft.Normalized();

        var errors = Validate(normalized, await repository.LoadAsync(cancellationToken).ConfigureAwait(false));
        if (errors.Count > 0)
        {
            return new AddProviderResult(null, errors, null);
        }

        var provider = new LlmProvider(Guid.NewGuid().ToString("N"), normalized.Name!, normalized.BaseUrl!, normalized.ApiKey!, normalized.Model!);
        var connection = await tester.TestAsync(provider, cancellationToken).ConfigureAwait(false);
        if (!connection.Success)
        {
            return new AddProviderResult(null, [], connection);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read again: the settings may have changed while the connection was tested.
            var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            errors = Validate(normalized, settings);
            if (errors.Count > 0)
            {
                return new AddProviderResult(null, errors, null);
            }

            settings.Add(provider);
            await repository.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            return new AddProviderResult(Summarize(provider, settings), [], connection);
        }
        finally
        {
            _lock.Release();
        }
    }

    // False when there is no such model. A deleted model that was chosen is unchosen; nothing is chosen in its place.
    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) =>
        ChangeAsync(settings => settings.Remove(id), cancellationToken);

    // id null: no model, which disables that use. False when there is no such model.
    public Task<bool> SelectForAnalysisAsync(string? id, CancellationToken cancellationToken) =>
        ChangeAsync(settings => TrySelect(() => settings.SelectForAnalysis(id)), cancellationToken);

    public Task<bool> SelectForChatAsync(string? id, CancellationToken cancellationToken) =>
        ChangeAsync(settings => TrySelect(() => settings.SelectForChat(id)), cancellationToken);

    // The model chosen for analyzing books, with its key, for the server to call. Null when none is chosen or its key
    // cannot be read: the analysis is then disabled.
    public async Task<LlmProvider?> GetAnalysisProviderAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        return settings.IsAnalysisEnabled ? settings.AnalysisProvider : null;
    }

    public async Task<LlmProvider?> GetChatProviderAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        return settings.IsChatEnabled ? settings.ChatProvider : null;
    }

    public void Dispose() => _lock.Dispose();

    private async Task<bool> ChangeAsync(Func<LlmSettings, bool> change, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!change(settings))
            {
                return false;
            }

            await repository.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool TrySelect(Action select)
    {
        try
        {
            select();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<FieldError> Validate(LlmProviderDraft normalized, LlmSettings settings) =>
        settings.Providers.Count >= LlmProviderLimits.MaxProviders
            ? [new FieldError(string.Empty, LlmProviderErrors.TooMany)]
            : LlmProviderValidator.Validate(normalized, settings.Providers.Select(provider => provider.Name));

    private static LlmProviderSummary Summarize(LlmProvider provider, LlmSettings settings) => new(
        provider.Id,
        provider.Name,
        provider.BaseUrl,
        provider.Model,
        HasKey: provider.ApiKey.Length > 0,
        KeyUnreadable: settings.UnreadableKeyProviderIds.Contains(provider.Id));
}
