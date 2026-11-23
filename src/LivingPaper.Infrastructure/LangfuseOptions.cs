using System.Text;

namespace LivingPaper.Infrastructure;

// Where the traces of the calls to the language models go (PAPER-75): a Langfuse, the user's own or the cloud one.
// The keys are secrets: a record would print them, so ToString is replaced and shows only the address.
public sealed record LangfuseOptions(Uri Endpoint, string PublicKey, string SecretKey, bool CaptureContent)
{
    // Langfuse accepts HTTP Basic: the public key as the user and the secret key as the password.
    public string AuthorizationHeader => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{PublicKey}:{SecretKey}"));

    // The address of the public API (".../api/public/"): the endpoint of the traces without its path, for the datasets and the scores.
    public Uri ApiBaseUri => new(Endpoint.AbsoluteUri[..^LangfuseSettings.TracesPath.Length] + "/api/public/");

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Endpoint = ").Append(Endpoint).Append(", CaptureContent = ").Append(CaptureContent);
        return true;
    }
}
