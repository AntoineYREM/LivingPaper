using System.Net.Http.Headers;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Infrastructure;

// Asks an OpenAI-compatible provider for its models: GET {baseUrl}/models, answer {"data":[{"id":"gpt-5"}, ...]}.
// The base URL is user input and this server calls it, as for the chat: http and https only, private addresses allowed on
// purpose (local models), no redirect followed (the key would go with it), a time limit, and a limit on what is read.
// A failure never carries the body of an HTTP error, the address or the key: only a kind and the HTTP status.
public sealed class OpenAiCompatibleModelCatalog : IModelCatalog, IDisposable
{
    // The most models kept: a provider with more (a gateway to hundreds of models) still gives a usable list.
    public const int MaxModels = 500;

    // The list of a large provider is some tens of kilobytes; anything much bigger is not a list.
    private const int MaxBytes = 2 * 1024 * 1024;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly SocketsHttpHandler _handler = new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };

    private readonly TimeSpan _timeout;

    public OpenAiCompatibleModelCatalog(TimeSpan? timeout = null) => _timeout = timeout ?? DefaultTimeout;

    public async Task<ModelListResult> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        if (!TryBuildAddress(baseUrl, out var address) || (apiKey ?? string.Empty).Any(char.IsControl))
        {
            // The client refuses the address or the key. The validator normally stops this earlier; no detail, it could repeat them.
            return ModelListResult.Failed(ConnectionFailure.Unreachable);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);
        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // The body is not read: an error page can echo the request, and so the key.
                return FailureOf((int)response.StatusCode);
            }

            await using var body = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            return await ReadModelsAsync(body, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The time limit, not the user giving up.
            return ModelListResult.Failed(ConnectionFailure.Unreachable);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return ModelListResult.Failed(ConnectionFailure.Unreachable);
        }
    }

    public void Dispose() => _handler.Dispose();

    private static ModelListResult FailureOf(int status) => status switch
    {
        401 or 403 => ModelListResult.Failed(ConnectionFailure.KeyRefused, status),
        404 or 405 or 501 => ModelListResult.Failed(ConnectionFailure.NoModelList, status),
        _ => ModelListResult.Failed(ConnectionFailure.ProviderError, status),
    };

    private static async Task<ModelListResult> ReadModelsAsync(Stream body, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return ModelListResult.Failed(ConnectionFailure.NoModelList);
            }

            buffer.Write(chunk, 0, read);
        }

        var models = ParseIds(buffer.ToArray());
        return models.Count == 0 ? ModelListResult.Failed(ConnectionFailure.NoModelList) : ModelListResult.Found(models);
    }

    private static List<string> ParseIds(byte[] json)
    {
        var ids = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return ids;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in data.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 } text
                    && text.Length <= LlmProviderLimits.Model
                    && !text.Any(char.IsControl)
                    && seen.Add(text))
                {
                    ids.Add(text);
                    if (ids.Count == MaxModels)
                    {
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            ids.Clear();
        }

        return ids;
    }

    // The same rule as the chat client: absolute http or https URL; "models" is added after the last segment.
    private static bool TryBuildAddress(string? baseUrl, out Uri address)
    {
        address = null!;
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        address = new Uri(builder.Uri, "models");
        return true;
    }
}
