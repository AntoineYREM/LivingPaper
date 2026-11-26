using LivingPaper.Domain;

namespace LivingPaper.Application;

// Checks that a language model works before it is saved: the address answers, the key is accepted, the model exists and
// the reply is usable. Faked in the tests of the settings, which never reach a real provider.
public interface ILlmConnectionTester
{
    // Neither throws for a failure of the provider: it is the result. Both throw OperationCanceledException when cancelled.

    // One short chat call.
    Task<ConnectionTestResult> TestAsync(LlmProvider provider, CancellationToken cancellationToken);

    // One short text turned into a vector: what an embedding model can do.
    Task<ConnectionTestResult> TestEmbeddingAsync(LlmProvider provider, CancellationToken cancellationToken);
}
