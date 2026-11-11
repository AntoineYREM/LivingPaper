using System.Text;

namespace LivingPaper.Domain;

// A language model the user configured: any OpenAI-compatible API (OpenAI, a local server, another vendor).
// The key is a secret and the base URL may carry credentials ("https://user:password@host"), so a record's automatic
// ToString (which prints every field, and ends up in logs and error pages) is replaced by one that prints neither.
public sealed record LlmProvider(string Id, string Name, string BaseUrl, string ApiKey, string Model)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", Name = ").Append(Name).Append(", Model = ").Append(Model);
        return true;
    }
}
