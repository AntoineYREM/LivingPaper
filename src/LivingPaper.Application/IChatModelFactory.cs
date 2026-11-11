using LivingPaper.Domain;

namespace LivingPaper.Application;

// Builds the client for one of the user's language models. The settings can change at any time, so the application
// asks for a client when it needs one instead of keeping one.
public interface IChatModelFactory
{
    // Throws ArgumentException when the provider's base URL is not an absolute http or https URL.
    // retryPassingFailures: false for a call where the answer must come at once, such as the connection test of the
    // settings: a wrong address would otherwise make the user wait through the retries (17 seconds) to see the error.
    IChatModel Create(LlmProvider provider, bool retryPassingFailures = true);
}
