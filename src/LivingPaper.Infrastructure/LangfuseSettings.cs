using Microsoft.Extensions.Configuration;

namespace LivingPaper.Infrastructure;

// Reads the four variables of Langfuse. Off (null) while the two keys are empty, so a .env.example with the address filled in and
// the keys empty changes nothing. A half-filled or invalid setting stops the application at start, naming the variable, rather than
// being ignored: a user who set the keys wants to see the traces, not to find out later that nothing is sent.
// LANGFUSE_BASE_URL has no default on purpose: the cloud address would send the traces to a third party without the user having
// chosen it.
public static class LangfuseSettings
{
    public const string BaseUrlVariable = "LANGFUSE_BASE_URL";
    public const string PublicKeyVariable = "LANGFUSE_PUBLIC_KEY";
    public const string SecretKeyVariable = "LANGFUSE_SECRET_KEY";
    public const string CaptureContentVariable = "LANGFUSE_CAPTURE_CONTENT";

    // Where the traces go, after the address of Langfuse. The other endpoints of its API (datasets, scores) are under /api/public/.
    public const string TracesPath = "/api/public/otel/v1/traces";

    public static LangfuseOptions? Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var publicKey = configuration[PublicKeyVariable]?.Trim();
        var secretKey = configuration[SecretKeyVariable]?.Trim();
        if (string.IsNullOrEmpty(publicKey) && string.IsNullOrEmpty(secretKey))
        {
            return null;
        }

        if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(secretKey))
        {
            // Never the value of the other key.
            throw new InvalidOperationException($"{(string.IsNullOrEmpty(publicKey) ? PublicKeyVariable : SecretKeyVariable)} is missing: Langfuse needs both keys.");
        }

        return new LangfuseOptions(ReadEndpoint(configuration), publicKey, secretKey, ReadCaptureContent(configuration));
    }

    private static Uri ReadEndpoint(IConfiguration configuration)
    {
        var text = configuration[BaseUrlVariable]?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException($"{BaseUrlVariable} is required with the Langfuse keys (for example http://localhost:3000).");
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("http" or "https") || string.IsNullOrEmpty(baseUrl.Host))
        {
            // The text is not repeated: it could carry a password.
            throw new InvalidOperationException($"{BaseUrlVariable} must be an absolute http or https URL.");
        }

        return new Uri(baseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/') + TracesPath);
    }

    private static bool ReadCaptureContent(IConfiguration configuration)
    {
        var text = configuration[CaptureContentVariable];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return bool.TryParse(text.Trim(), out var value)
            ? value
            : throw new InvalidOperationException($"{CaptureContentVariable} must be true or false.");
    }
}
