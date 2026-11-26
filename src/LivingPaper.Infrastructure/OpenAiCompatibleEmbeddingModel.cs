using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Infrastructure;

// Asks any OpenAI-compatible provider for vectors: POST {baseUrl}/embeddings {"model", "input": [texts]}, answer
// {"data": [{"index": 0, "embedding": [numbers]}], "usage": {"prompt_tokens": n}}.
// One call, no retry: RetryingEmbeddingModel adds it. The answer is untrusted input: it is limited in size and checked
// before use. Messages never contain the body of an HTTP error, the URL, the key or any text.
// httpClient: its BaseAddress is the provider's base URL ending with "/", it follows no redirect and has no timeout.
public sealed class OpenAiCompatibleEmbeddingModel : IEmbeddingModel
{
    // Far above the largest vectors of today's models (a few thousand numbers).
    public const int MaxDimensions = 16_384;

    public const int MaxResponseBytes = 32 * 1024 * 1024;

    // Strict numbers: the web defaults would also accept a number written as a text.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    };

    private readonly HttpClient _httpClient;
    private readonly LlmProvider _provider;
    private readonly OpenAiCompatibleOptions _options;

    public OpenAiCompatibleEmbeddingModel(HttpClient httpClient, LlmProvider provider, OpenAiCompatibleOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        if (provider.ApiKey.Any(char.IsControl))
        {
            // Never say which key: the message could reach a log or a page.
            throw new ArgumentException("The API key contains a control character (a line break, for instance).", nameof(provider));
        }

        _httpClient = httpClient;
        _provider = provider;
        _options = options;
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(new RequestBody(_provider.Model, texts), options: JsonOptions),
        };

        // A local server (Ollama, LM Studio) may need no key: then no header is sent.
        if (!string.IsNullOrEmpty(_provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _provider.ApiKey);
        }

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The body is not read: an error page can echo the request, and so the key.
            throw new ChatProviderException(ChatFailureKind.Http, $"The embedding model answered HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
        }

        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        return Parse(body, texts.Count, usage);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HeadersTimeout);
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatProviderException(ChatFailureKind.TimedOut, "The embedding model did not answer in time.");
        }
        catch (HttpRequestException)
        {
            // No detail: it could name the address, which may carry credentials.
            throw new ChatProviderException(ChatFailureKind.Unreachable, "The embedding model could not be reached.");
        }
    }

    private async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                // The time limit counts the silence: it starts again at each piece.
                timeout.CancelAfter(_options.IdleTimeout);
                var read = await body.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
                }

                if (buffer.Length + read > MaxResponseBytes)
                {
                    throw Invalid("The answer of the embedding model is too large.");
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatProviderException(ChatFailureKind.TimedOut, "The embedding model stopped sending its answer.");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new ChatProviderException(ChatFailureKind.StreamCut, "The connection to the embedding model was lost before the end of its answer.");
        }
    }

    private static float[][] Parse(ReadOnlyMemory<byte> body, int expected, ChatUsageMeter? usage)
    {
        ResponseBody? answer;
        try
        {
            answer = JsonSerializer.Deserialize<ResponseBody>(body.Span, JsonOptions);
        }
        catch (JsonException)
        {
            throw Invalid("The answer of the embedding model could not be read.");
        }

        // The tokens were spent even when the vectors turn out to be unusable.
        usage?.Add(Math.Max(0, answer?.Usage?.PromptTokens ?? 0), 0, 0);

        if (answer?.Data is not { } data || data.Count != expected)
        {
            throw Invalid("The embedding model did not give one vector for each text.");
        }

        var vectors = new float[expected][];
        for (var position = 0; position < data.Count; position++)
        {
            var index = data[position]?.Index ?? position;
            if (index < 0 || index >= expected || vectors[index] is not null)
            {
                throw Invalid("The embedding model numbered its vectors in a way that cannot be followed.");
            }

            var vector = data[position]?.Embedding;
            if (vector is null || vector.Length == 0 || vector.Length > MaxDimensions
                || !Array.TrueForAll(vector, float.IsFinite)
                || Array.TrueForAll(vector, number => number == 0))
            {
                throw Invalid("The embedding model gave a vector that cannot be used.");
            }

            vectors[index] = vector;
        }

        if (vectors.Any(vector => vector.Length != vectors[0].Length))
        {
            throw Invalid("The embedding model gave vectors of different sizes.");
        }

        return vectors;
    }

    private static ChatProviderException Invalid(string message) => new(ChatFailureKind.InvalidResponse, message);

    private sealed record RequestBody(string Model, IReadOnlyList<string> Input);

    private sealed record ResponseBody(List<VectorData?>? Data, UsageData? Usage);

    private sealed record VectorData(int? Index, float[]? Embedding);

    private sealed record UsageData([property: System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")] int? PromptTokens);
}
