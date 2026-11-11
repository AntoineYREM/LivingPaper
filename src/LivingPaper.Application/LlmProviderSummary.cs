namespace LivingPaper.Application;

// A language model as the settings page shows it. It never carries the key: the key goes from the browser to the server
// when the model is added, and never back. HasKey tells whether one is saved; KeyUnreadable that it was saved but can
// no longer be decrypted (the encryption keys of the application were lost), so the model must be added again.
public sealed record LlmProviderSummary(string Id, string Name, string BaseUrl, string Model, bool HasKey, bool KeyUnreadable);
