using LivingPaper.Domain;

namespace LivingPaper.Application;

// Checks that a language model works before it is saved: the address answers, the key is accepted, the model exists and
// the reply is usable. Faked in the tests of the settings, which never reach a real provider.
public interface ILlmConnectionTester
{
    // Never throws for a failure of the provider: it is the result. Throws OperationCanceledException when cancelled.
    Task<ConnectionTestResult> TestAsync(LlmProvider provider, CancellationToken cancellationToken);
}
