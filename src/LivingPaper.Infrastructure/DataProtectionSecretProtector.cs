using System.Security.Cryptography;
using LivingPaper.Application;
using Microsoft.AspNetCore.DataProtection;

namespace LivingPaper.Infrastructure;

// Encrypts API keys with ASP.NET Core Data Protection (authenticated encryption). The text it returns is URL-safe base64:
// safe to write in a JSON file. The encryption keys are kept by the provider the application gives it (data/keys), so a
// key survives a restart. The purpose string separates these secrets from anything else protected by the same keys.
// Be honest about the limit (it is in the README): where the application cannot protect its own keys (Docker on Linux),
// they sit in the same data volume as settings.json. The encryption protects a settings.json that leaks alone.
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    // Changing it would make every saved key unreadable: it is part of the format of settings.json.
    private const string Purpose = "LivingPaper.LlmApiKeys.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return _protector.Protect(secret);
    }

    public bool TryUnprotect(string protectedText, out string secret)
    {
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return false;
        }

        try
        {
            secret = _protector.Unprotect(protectedText);
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // Not protected by this application, altered, or protected with keys that are gone. Never say more: the
            // message of the exception could include part of the text.
            return false;
        }
    }
}
