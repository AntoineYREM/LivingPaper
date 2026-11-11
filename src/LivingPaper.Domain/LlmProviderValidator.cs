namespace LivingPaper.Domain;

// The rules a language model must follow before it is tested and saved. Private addresses (localhost, 192.168.x.x,
// host.docker.internal) are allowed on purpose, so local models (Ollama, LM Studio) work: the application is meant to be
// run by one person, and the README says it must not be exposed to the Internet without protection.
public static class LlmProviderValidator
{
    // existingNames: the names already used, to refuse a duplicate (ignoring case and the spaces around).
    // The errors come in the order of the form: name, base URL, key, model.
    public static IReadOnlyList<FieldError> Validate(LlmProviderDraft draft, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(existingNames);
        var normalized = draft.Normalized();
        var errors = new List<FieldError>();

        Add(errors, "name", CheckName(normalized.Name!, existingNames));
        Add(errors, "baseUrl", CheckBaseUrl(normalized.BaseUrl!));
        Add(errors, "apiKey", CheckApiKey(normalized.ApiKey!));
        Add(errors, "model", CheckModel(normalized.Model!));
        return errors;
    }

    private static void Add(List<FieldError> errors, string field, string? code)
    {
        if (code is not null)
        {
            errors.Add(new FieldError(field, code));
        }
    }

    private static string? CheckName(string name, IEnumerable<string> existingNames)
    {
        if (name.Length == 0)
        {
            return LlmProviderErrors.Required;
        }

        if (name.Length > LlmProviderLimits.Name)
        {
            return LlmProviderErrors.TooLong;
        }

        if (name.Any(char.IsControl))
        {
            return LlmProviderErrors.InvalidCharacters;
        }

        return existingNames.Any(existing => string.Equals(existing?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            ? LlmProviderErrors.Taken
            : null;
    }

    private static string? CheckBaseUrl(string url)
    {
        if (url.Length == 0)
        {
            return LlmProviderErrors.Required;
        }

        if (url.Length > LlmProviderLimits.BaseUrl)
        {
            return LlmProviderErrors.TooLong;
        }

        // On Linux "/v1" is read as a file path ("file:///v1"): the text itself must start with the scheme.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !url.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase)
            || (uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.Host)))
        {
            return LlmProviderErrors.Invalid;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return LlmProviderErrors.Scheme;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return LlmProviderErrors.Credentials;
        }

        return !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            ? LlmProviderErrors.QueryOrFragment
            : null;
    }

    // An empty key is fine: a local server needs none. A control character (a line break in the middle) would break the
    // Authorization header.
    private static string? CheckApiKey(string key)
    {
        if (key.Length > LlmProviderLimits.ApiKey)
        {
            return LlmProviderErrors.TooLong;
        }

        return key.Any(char.IsControl) ? LlmProviderErrors.InvalidCharacters : null;
    }

    private static string? CheckModel(string model)
    {
        if (model.Length == 0)
        {
            return LlmProviderErrors.Required;
        }

        if (model.Length > LlmProviderLimits.Model)
        {
            return LlmProviderErrors.TooLong;
        }

        return model.Any(char.IsControl) ? LlmProviderErrors.InvalidCharacters : null;
    }
}
