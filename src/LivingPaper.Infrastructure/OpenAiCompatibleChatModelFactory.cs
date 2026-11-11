using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Infrastructure;

// Builds the client of one of the user's language models: the HTTP adapter wrapped in the retries.
// All the clients share one connection pool (one handler), which follows no redirect: a provider that answers
// "go to another address" would otherwise receive the API key there. Not IHttpClientFactory: its default logging
// writes the address of every request, and a base URL can carry credentials.
// The base URL is user input and this server calls it: only http and https are accepted. Private addresses
// (localhost, 192.168.x.x, host.docker.internal) are allowed on purpose, so local models work; the application is
// meant to be run by one person and must not be exposed to the Internet without protection (see the README).
public sealed class OpenAiCompatibleChatModelFactory : IChatModelFactory, IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly OpenAiCompatibleOptions _options;
    private readonly SocketsHttpHandler _handler;

    public OpenAiCompatibleChatModelFactory(ILoggerFactory loggerFactory, OpenAiCompatibleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _options = options ?? new OpenAiCompatibleOptions();
        _handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = _options.ConnectTimeout,
            UseCookies = false,

            // Picks up a changed DNS address from time to time instead of keeping a connection for ever.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
    }

    public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var baseAddress = ParseBaseAddress(provider.BaseUrl);

        // Timeout: none. The adapter applies its own limits; HttpClient's would also cut a long streaming reply.
        // Not disposed here: the handler (the only thing that holds resources) is shared and owned by the factory.
        var client = new HttpClient(_handler, disposeHandler: false) { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        var model = new OpenAiCompatibleChatModel(client, provider, _options);
        return retryPassingFailures
            ? new RetryingChatModel(model, _options.RetryDelays, _loggerFactory.CreateLogger<RetryingChatModel>())
            : model;
    }

    public void Dispose() => _handler.Dispose();

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
