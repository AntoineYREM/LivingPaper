using LivingPaper.Domain;

namespace LivingPaper.Application;

// Builds the embedding client of one of the user's models, as IChatModelFactory does for the chat.
public interface IEmbeddingModelFactory
{
    // Throws ArgumentException when the provider's base URL is not an absolute http or https URL.
    // retryPassingFailures: false for a call where the answer must come at once, such as the test of the settings.
    IEmbeddingModel Create(LlmProvider provider, bool retryPassingFailures = true);
}
