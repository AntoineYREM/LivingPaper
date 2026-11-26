using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Infrastructure;

// Builds the client of one of the user's language models: the HTTP adapter, the tracing of its calls (PAPER-75), the retries.
// All the clients share one connection pool; the rules about the address are in ProviderHttp.
public sealed class OpenAiCompatibleChatModelFactory : IChatModelFactory, IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly OpenAiCompatibleOptions _options;
    private readonly LlmTracingOptions _tracing;
    private readonly SocketsHttpHandler _handler;

    public OpenAiCompatibleChatModelFactory(ILoggerFactory loggerFactory, OpenAiCompatibleOptions? options = null, LlmTracingOptions? tracing = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _options = options ?? new OpenAiCompatibleOptions();
        _tracing = tracing ?? new LlmTracingOptions();
        _handler = ProviderHttp.CreateHandler(_options);
    }

    public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var client = ProviderHttp.CreateClient(_handler, provider.BaseUrl);

        // The tracing is below the retries: each try is recorded on its own, the failed ones as errors.
        IChatModel model = new TracingChatModel(new OpenAiCompatibleChatModel(client, provider, _options), provider, _tracing);
        return retryPassingFailures
            ? new RetryingChatModel(model, _options.RetryDelays, _loggerFactory.CreateLogger<RetryingChatModel>())
            : model;
    }

    public void Dispose() => _handler.Dispose();
}
