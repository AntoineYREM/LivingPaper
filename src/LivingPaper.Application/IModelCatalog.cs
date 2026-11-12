namespace LivingPaper.Application;

// Asks a provider which models it offers (GET {baseUrl}/models). Faked in the tests, which never reach a real provider.
public interface IModelCatalog
{
    // Never throws for a failure of the provider: it is the result. Throws OperationCanceledException when cancelled.
    // apiKey: empty for a local server that needs none.
    Task<ModelListResult> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken);
}
