namespace LivingPaper.Application;

// Why the connection test of a language model failed, in terms of what the user can fix on the settings page.
public enum ConnectionFailure
{
    // Nothing answers at the address, or not in time.
    Unreachable,

    // The provider refused the key (HTTP 401 or 403).
    KeyRefused,

    // The provider does not know the model (HTTP 404, or 400 for the providers that answer that way).
    ModelUnknown,

    // The provider answered, but not with a usable reply: unreadable, empty, cut or filtered.
    InvalidResponse,

    // Another error of the provider (busy, failing, redirecting...). The HTTP status is given.
    ProviderError,

    // The prompts file cannot be used, so the test message could not be built. Detail says what to fix.
    PromptFile,
}
