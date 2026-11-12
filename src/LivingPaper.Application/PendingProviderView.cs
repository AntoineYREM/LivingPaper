namespace LivingPaper.Application;

// A language model being added: what the user typed (without the key) and the models the provider offered. The key stays
// in PendingProviderStore, on the server. Models is empty when the provider has no list: the user types the model then.
public sealed record PendingProviderView(string Id, string Name, string BaseUrl, IReadOnlyList<string> Models)
{
    public bool CanChooseFromList => Models.Count > 0;
}
