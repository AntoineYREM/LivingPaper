namespace LivingPaper.Infrastructure;

// What the chat client and the embedding client share to reach a provider.
// The base URL is user input and this server calls it: only http and https are accepted. Private addresses
// (localhost, 192.168.x.x, host.docker.internal) are allowed on purpose, so local models work; the application is
// meant to be run by one person and must not be exposed to the Internet without protection (see the README).
// Not IHttpClientFactory: its default logging writes the address of every request, and a base URL can carry credentials.
internal static class ProviderHttp
{
    // One connection pool, which follows no redirect: a provider that answers "go to another address" would otherwise
    // receive the API key there.
    public static SocketsHttpHandler CreateHandler(OpenAiCompatibleOptions options) => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = options.ConnectTimeout,
        UseCookies = false,

        // Picks up a changed DNS address from time to time instead of keeping a connection for ever.
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };

    // Timeout: none. The clients apply their own limits; HttpClient's would also cut a long streaming reply.
    // The client is not disposed by its user: the handler (the only thing that holds resources) is shared and owned by the factory.
    public static HttpClient CreateClient(SocketsHttpHandler handler, string baseUrl) =>
        new(handler, disposeHandler: false) { BaseAddress = ParseBaseAddress(baseUrl), Timeout = Timeout.InfiniteTimeSpan };

    // Ends with "/" so that "chat/completions" is added after the last segment ("…/v1" and "…/v1/" both work).
    private static Uri ParseBaseAddress(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            // The text is not repeated: it may carry a password.
            throw new ArgumentException("The base URL of the language model must be an absolute http or https URL.", nameof(baseUrl));
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}
