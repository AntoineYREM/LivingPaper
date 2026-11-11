namespace LivingPaper.Application;

// Encrypts the API keys before they are written to disk, and decrypts them when the application needs to use them.
// The protected text is safe to store in settings.json; it is only useful to the application that has the keys.
public interface ISecretProtector
{
    string Protect(string secret);

    // False when the text is not something this application protected, was altered, or was protected with encryption
    // keys that are no longer there. The secret is then empty.
    bool TryUnprotect(string protectedText, out string secret);
}
