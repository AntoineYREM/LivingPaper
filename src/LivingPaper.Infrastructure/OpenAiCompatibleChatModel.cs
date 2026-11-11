using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Infrastructure;

// Talks to any OpenAI-compatible provider: POST {baseUrl}/chat/completions with "stream": true. The answer is read as
// server-sent events ("data: {json}" lines ending with "data: [DONE]"), so the chat can show the first words after a
// few seconds, and a long analysis never waits on a silent connection.
// One call, no retry: RetryingChatModel adds it. Messages never contain the body of an HTTP error, the URL, the key or
// any text of a book or a conversation.
// httpClient: its BaseAddress is the provider's base URL ending with "/", it follows no redirect, and it has no timeout
// of its own: this class applies the limits in "options" (a streaming reply can last longer than any single timeout).
public sealed class OpenAiCompatibleChatModel : IChatModel
{
    // Web defaults give camelCase names; the names the API wants that are not camelCase are set one by one.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly LlmProvider _provider;
    private readonly OpenAiCompatibleOptions _options;

    public OpenAiCompatibleChatModel(HttpClient httpClient, LlmProvider provider, OpenAiCompatibleOptions options)
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

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        var reply = new StringBuilder();
        await foreach (var piece in StreamReplyAsync(messages, usage, cancellationToken).ConfigureAwait(false))
        {
            reply.Append(piece);
        }

        return reply.ToString();
    }

    // [EnumeratorCancellation] passes the token given to "await foreach" into this method.
    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        using var request = BuildRequest(messages);

        // ResponseHeadersRead: start reading as soon as the first bytes arrive, not when the whole reply is there.
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The body is not read: an error page can echo the request, and so the key.
            throw new ChatProviderException(ChatFailureKind.Http, $"The language model answered HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
        }

        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            throw new ChatProviderException(ChatFailureKind.InvalidResponse, "The language model did not stream its reply as asked (it answered with plain JSON).");
        }

        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(body, Encoding.UTF8);
        var gotText = false;
        var finished = false;
        while (await ReadLineAsync(reader, cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue; // Blank lines separate the events; ": comments" and "event:" lines are not data.
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                finished = true;
                break;
            }

            var chunk = ParseChunk(data);
            if (chunk?.Usage is { } counts)
            {
                usage?.Add(NonNegative(counts.PromptTokens), NonNegative(counts.PromptTokensDetails?.CachedTokens), NonNegative(counts.CompletionTokens));
            }

            var choice = chunk?.Choices?.FirstOrDefault();

            // A filtered reply stops with finish_reason "content_filter", sometimes after a few words: it is not an answer.
            if (choice?.FinishReason == "content_filter")
            {
                throw new ContentFilteredException();
            }

            if (!string.IsNullOrEmpty(choice?.Delta?.Content))
            {
                gotText = true;
                yield return choice.Delta.Content;
            }
        }

        // Without "[DONE]" the connection ended too early (a time limit, a network problem): what arrived is only the
        // start of the reply, and must not be taken for the whole of it.
        if (!finished)
        {
            throw new ChatProviderException(ChatFailureKind.StreamCut, "The reply of the language model ended before its end.");
        }

        if (!gotText)
        {
            throw new ChatProviderException(ChatFailureKind.EmptyReply, "The language model answered with no text.");
        }
    }

    private HttpRequestMessage BuildRequest(IReadOnlyList<ChatMessage> messages)
    {
        var body = new RequestBody(
            _provider.Model,
            [.. messages.Select(message => new RequestMessage(message.Role.ToString().ToLowerInvariant(), message.Content))],
            Stream: true,
            new StreamOptions(IncludeUsage: true));
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body, options: JsonOptions) };

        // A local server (Ollama, LM Studio) may need no key: then no header is sent.
        if (!string.IsNullOrEmpty(_provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _provider.ApiKey);
        }

        return request;
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
            // Our time limit, or the connection time limit of the handler: not the caller giving up.
            throw new ChatProviderException(ChatFailureKind.TimedOut, "The language model did not answer in time.");
        }
        catch (HttpRequestException)
        {
            // No detail: it could name the address, which may carry credentials.
            throw new ChatProviderException(ChatFailureKind.Unreachable, "The language model could not be reached.");
        }
    }

    private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.IdleTimeout);
        try
        {
            return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatProviderException(ChatFailureKind.TimedOut, "The language model stopped sending its reply.");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new ChatProviderException(ChatFailureKind.StreamCut, "The connection to the language model was lost before the end of its reply.");
        }
    }

    // A separate method: C# does not allow "yield return" in a block that has a "catch".
    private static StreamChunk? ParseChunk(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<StreamChunk>(data, JsonOptions);
        }
        catch (JsonException)
        {
            // Not the text of the event: it holds the reply.
            throw new ChatProviderException(ChatFailureKind.InvalidResponse, "The language model sent a part of its reply that could not be read.");
        }
    }

    private static int NonNegative(int? count) => Math.Max(0, count ?? 0);

    private sealed record RequestBody(
        string Model,
        IReadOnlyList<RequestMessage> Messages,
        bool Stream,
        [property: JsonPropertyName("stream_options")] StreamOptions StreamOptions);

    private sealed record StreamOptions([property: JsonPropertyName("include_usage")] bool IncludeUsage);

    private sealed record RequestMessage(string Role, string Content);

    private sealed record StreamChunk(List<StreamChoice>? Choices, UsageData? Usage);

    private sealed record StreamChoice(StreamDelta? Delta, [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record StreamDelta(string? Content);

    private sealed record UsageData(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("prompt_tokens_details")] PromptDetails? PromptTokensDetails);

    private sealed record PromptDetails([property: JsonPropertyName("cached_tokens")] int? CachedTokens);
}
