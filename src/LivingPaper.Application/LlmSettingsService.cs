using LivingPaper.Domain;

namespace LivingPaper.Application;

// The use cases of the settings page: list the language models, add one, delete one, and choose the one for analyzing books
// and the one for chatting.
// A model is added in two steps (PAPER-26): "get the models" asks the provider for its list, which proves that the address
// and the key work, and keeps the draft, key included, in memory; "save" stores the model the user chose, with no other test.
// A provider with no list of models keeps the older way: the user types the model, and a connection test runs before saving.
// Changes are made one at a time (a lock around load, change, save) so two requests never overwrite each other. The
// connection test and the list, which can take seconds, are made outside the lock.
// A key is only ever returned by GetAnalysisProviderAsync and GetChatProviderAsync, for the server to call the model.
public sealed class LlmSettingsService(
    ILlmSettingsRepository repository, ILlmConnectionTester tester, IModelCatalog catalog, PendingProviderStore pending) : IDisposable
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

    // The way for a provider with no list of models: the model is typed, tested with a real call, then saved.
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

        return await SaveAsync(normalized, provider, connection, cancellationToken).ConfigureAwait(false);
    }

    // Step 1: asks the provider for its models and keeps the draft. Nothing is saved.
    public async Task<FetchModelsResult> FetchModelsAsync(LlmProviderDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var normalized = draft.Normalized();

        var errors = Validate(normalized, await repository.LoadAsync(cancellationToken).ConfigureAwait(false), includeModel: false);
        if (errors.Count > 0)
        {
            return new FetchModelsResult(null, errors, null, null);
        }

        var list = await catalog.ListAsync(normalized.BaseUrl!, normalized.ApiKey!, cancellationToken).ConfigureAwait(false);
        if (list.Success)
        {
            var models = list.Models.Order(StringComparer.OrdinalIgnoreCase).ToList();
            return new FetchModelsResult(pending.Add(normalized.Name!, normalized.BaseUrl!, normalized.ApiKey!, models), [], null, null);
        }

        if (list.Failure == ConnectionFailure.NoModelList)
        {
            // No list: the draft is kept with no model, and the user types one (the connection test runs when saving).
            return new FetchModelsResult(pending.Add(normalized.Name!, normalized.BaseUrl!, normalized.ApiKey!, []), [], null, null);
        }

        return new FetchModelsResult(null, [], list.Failure, list.HttpStatus);
    }

    // The draft for the page, without its key. Null when there is none (unknown, expired or already saved).
    public PendingProviderView? GetPending(string? id) => pending.Get(id);

    // Step 2: saves the model the user chose. Null when the draft is gone (expired, or the application restarted): the user
    // starts again. The model must be one of those the provider offered, and is not tested again: the list proved the key.
    // Without a list (the provider has none), the model typed by the user goes through the connection test. A refused choice
    // keeps the draft, so the user can choose again; a saved model removes it, and the key with it.
    public async Task<AddProviderResult?> AddFetchedAsync(string? id, string? model, CancellationToken cancellationToken)
    {
        if (pending.Take(id) is not var (view, apiKey))
        {
            return null;
        }

        var chosen = model?.Trim() ?? string.Empty;
        AddProviderResult result;
        if (!view.CanChooseFromList)
        {
            result = await AddAsync(new LlmProviderDraft(view.Name, view.BaseUrl, apiKey, chosen), cancellationToken).ConfigureAwait(false);
        }
        else if (!view.Models.Contains(chosen, StringComparer.Ordinal))
        {
            var code = chosen.Length == 0 ? LlmProviderErrors.Required : LlmProviderErrors.NotInList;
            return new AddProviderResult(null, [new FieldError("model", code)], null);
        }
        else
        {
            var normalized = new LlmProviderDraft(view.Name, view.BaseUrl, apiKey, chosen);
            var errors = Validate(normalized, await repository.LoadAsync(cancellationToken).ConfigureAwait(false));
            if (errors.Count > 0)
            {
                return new AddProviderResult(null, errors, null);
            }

            var provider = new LlmProvider(Guid.NewGuid().ToString("N"), view.Name, view.BaseUrl, apiKey, chosen);
            result = await SaveAsync(normalized, provider, ConnectionTestResult.Ok(), cancellationToken).ConfigureAwait(false);
        }

        if (result.Succeeded)
        {
            pending.Remove(id);
        }

        return result;
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

    private async Task<AddProviderResult> SaveAsync(LlmProviderDraft normalized, LlmProvider provider, ConnectionTestResult connection, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read again: the settings may have changed while the provider was asked.
            var settings = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            var errors = Validate(normalized, settings);
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

    private static IReadOnlyList<FieldError> Validate(LlmProviderDraft normalized, LlmSettings settings, bool includeModel = true) =>
        settings.Providers.Count >= LlmProviderLimits.MaxProviders
            ? [new FieldError(string.Empty, LlmProviderErrors.TooMany)]
            : LlmProviderValidator.Validate(normalized, settings.Providers.Select(provider => provider.Name), includeModel);

    private static LlmProviderSummary Summarize(LlmProvider provider, LlmSettings settings) => new(
        provider.Id,
        provider.Name,
        provider.BaseUrl,
        provider.Model,
        HasKey: provider.ApiKey.Length > 0,
        KeyUnreadable: settings.UnreadableKeyProviderIds.Contains(provider.Id));
}
