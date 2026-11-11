namespace LivingPaper.Domain;

// A form field that cannot be accepted. Field is the name of the form field ("name", "baseUrl", "apiKey", "model") and
// Code a stable key the interface translates (see LlmProviderErrors). It never contains what the user typed: it could
// be a key or a URL with a password.
public sealed record FieldError(string Field, string Code);
