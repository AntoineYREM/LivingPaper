using LivingPaper.Domain;

namespace LivingPaper.Application;

// Builds the client for one of the user's language models. The settings can change at any time, so the application
// asks for a client when it needs one instead of keeping one.
public interface IChatModelFactory
{
    // Throws ArgumentException when the provider's base URL is not an absolute http or https URL.
    IChatModel Create(LlmProvider provider);
}
