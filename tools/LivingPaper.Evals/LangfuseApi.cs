using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LivingPaper.Infrastructure;

namespace LivingPaper.Evals;

// The REST API of Langfuse, for what OpenTelemetry cannot carry: the dataset and its items, and the scores. (Langfuse has no .NET SDK.)
// Endpoints (https://api.reference.langfuse.com): GET and POST /v2/datasets, POST /dataset-items (an item is created or updated by its
// id), POST /scores (a list; the scores are accepted asynchronously).
// The address and the keys are user input and secrets: redirects are not followed, there is a time limit, and an error says the status
// and what was being done, never the answer of the server (it could repeat a key) nor the text of the exception (it holds the address).
internal sealed class LangfuseApi : IDisposable
{
    private const int ScoreBatchSize = 50;

    private readonly HttpClient _client;

    public LangfuseApi(LangfuseOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        handler ??= new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        _client = new HttpClient(handler, disposeHandler: true) { BaseAddress = options.ApiBaseUri, Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(options.AuthorizationHeader);
    }

    // The id of the dataset of this name: the existing one, or a new one. Run again, it finds the same.
    public async Task<string> EnsureDatasetAsync(string name, string description, CancellationToken cancellationToken)
    {
        var found = await SendAsync(HttpMethod.Get, $"v2/datasets/{Uri.EscapeDataString(name)}", body: null, cancellationToken).ConfigureAwait(false);
        if (found.Status == HttpStatusCode.NotFound)
        {
            found = await SendAsync(HttpMethod.Post, "v2/datasets", new { name, description }, cancellationToken).ConfigureAwait(false);
        }

        Require(found.Status, "creating the dataset");
        return ReadId(found.Body);
    }

    // The question as an item of the dataset. The id is stable, so sending it again updates the item.
    public async Task UpsertItemAsync(string datasetName, EvalQuestion question, CancellationToken cancellationToken)
    {
        var answer = await SendAsync(
            HttpMethod.Post,
            "dataset-items",
            new
            {
                id = LangfuseNames.ItemId(datasetName, question.Id),
                datasetName,
                input = question.Input,
                expectedOutput = question.ExpectedOutput,
                metadata = new { category = question.Category, questionId = question.Id },
            },
            cancellationToken).ConfigureAwait(false);
        Require(answer.Status, "sending a question");
    }

    public async Task PostScoresAsync(IReadOnlyList<LangfuseScore> scores, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scores);
        foreach (var batch in scores.Chunk(ScoreBatchSize))
        {
            var body = batch.Select(score => new
            {
                id = score.Id,
                traceId = score.TraceId,
                observationId = score.ObservationId,
                name = score.Name,
                value = score.Value,
                dataType = "NUMERIC",
                comment = score.Comment,
                environment = score.Environment,
                metadata = new { judgePromptVersion = score.JudgePromptVersion },
            });
            var answer = await SendAsync(HttpMethod.Post, "scores", body, cancellationToken).ConfigureAwait(false);
            Require(answer.Status, "sending the scores");
            RequireAllAccepted(answer);
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new EvalException("Langfuse could not be reached: check LANGFUSE_BASE_URL and that Langfuse is running.");
        }
    }

    private static void Require(HttpStatusCode status, string doing)
    {
        if ((int)status is >= 200 and < 300 or 207)
        {
            return;
        }

        throw new EvalException(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? $"Langfuse refused the keys (HTTP {(int)status}) while {doing}: check LANGFUSE_PUBLIC_KEY and LANGFUSE_SECRET_KEY."
            : $"Langfuse answered HTTP {(int)status} while {doing}.");
    }

    // 207: some scores were accepted and some not. Only the counts are reported: the messages come from the server.
    private static void RequireAllAccepted((HttpStatusCode Status, string Body) answer)
    {
        if (answer.Status != HttpStatusCode.MultiStatus)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(answer.Body);
            if (document.RootElement.TryGetProperty("rejected", out var rejected) && rejected.TryGetInt32(out var count) && count > 0)
            {
                throw new EvalException($"Langfuse rejected {count} of the scores.");
            }
        }
        catch (JsonException)
        {
            throw new EvalException("Langfuse answered the scores with a status 207 that could not be read.");
        }
    }

    private static string ReadId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString()))
            {
                return id.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Falls through to the same error.
        }

        throw new EvalException("Langfuse answered the dataset with something unexpected.");
    }
}
