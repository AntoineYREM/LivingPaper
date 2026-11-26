namespace LivingPaper.Application;

// An embedding model reached through the OpenAI-compatible embeddings API: it turns texts into vectors, so that passages
// close in meaning to a question can be found. Faked in tests, which never call a real provider.
public interface IEmbeddingModel
{
    // One vector for each text, in the order of the texts. All of them have the same size, hold finite numbers and are not
    // all zeros. No text: no call. Throws ChatProviderException on failure (the failures of a call to a provider), after
    // retrying the passing ones.
    // usage: when given, receives the tokens of the call if the provider reports them.
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken);
}
