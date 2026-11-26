using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Infrastructure;

// Builds the embedding client of one of the user's models: the HTTP adapter and the retries, with the same options and the
// same rules about the address as the chat (ProviderHttp).
public sealed class OpenAiCompatibleEmbeddingModelFactory : IEmbeddingModelFactory, IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly OpenAiCompatibleOptions _options;
    private readonly SocketsHttpHandler _handler;

    public OpenAiCompatibleEmbeddingModelFactory(ILoggerFactory loggerFactory, OpenAiCompatibleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _options = options ?? new OpenAiCompatibleOptions();
        _handler = ProviderHttp.CreateHandler(_options);
    }

    public IEmbeddingModel Create(LlmProvider provider, bool retryPassingFailures = true)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var client = ProviderHttp.CreateClient(_handler, provider.BaseUrl);
        IEmbeddingModel model = new OpenAiCompatibleEmbeddingModel(client, provider, _options);
        return retryPassingFailures
            ? new RetryingEmbeddingModel(model, _options.RetryDelays, _loggerFactory.CreateLogger<RetryingEmbeddingModel>())
            : model;
    }

    public void Dispose() => _handler.Dispose();
}
