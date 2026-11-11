using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LivingPaper.Infrastructure.Tests;

// A small real HTTP server (Kestrel, on a free local port) that plays the part of an OpenAI-compatible provider.
// It never reaches a real provider. Set Handler to decide what the next requests get.
internal sealed class FakeLlmServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<RecordedRequest> _requests = [];

    private FakeLlmServer(WebApplication app, string address)
    {
        _app = app;
        BaseUrl = address + "/v1";
    }

    // "http://127.0.0.1:PORT/v1": what a user would type as the base URL.
    public string BaseUrl { get; }

    public Func<HttpContext, Task> Handler { get; set; } = context =>
    {
        context.Response.StatusCode = 500;
        return Task.CompletedTask;
    };

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public int ElsewhereHits { get; private set; }

    public static async Task<FakeLlmServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        var holder = new ServerHolder();
        app.MapPost("/v1/chat/completions", context => holder.Server!.HandleAsync(context));
        app.Map("/elsewhere/v1/chat/completions", context =>
        {
            holder.Server!.ElsewhereHits++;
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        holder.Server = new FakeLlmServer(app, address);
        return holder.Server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        lock (_requests)
        {
            _requests.Add(new RecordedRequest(
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.Request.Headers.Authorization.ToString(),
                context.Request.ContentType,
                body));
        }

        await Handler(context);
    }

    private sealed class ServerHolder
    {
        public FakeLlmServer? Server { get; set; }
    }
}

internal sealed record RecordedRequest(string Method, string Path, string Authorization, string? ContentType, string Body)
{
    public JsonDocument Json() => JsonDocument.Parse(Body);
}

// Ready-made answers, in the format of the OpenAI Chat Completions API with "stream": true.
internal static class FakeReplies
{
    // "data: {json}" events, one per piece, then "data: [DONE]". The headers go out with the first flush.
    public static async Task StreamAsync(HttpContext context, IEnumerable<string> events, bool done = true, string contentType = "text/event-stream")
    {
        context.Response.ContentType = contentType;
        await context.Response.StartAsync(context.RequestAborted);
        foreach (var data in events)
        {
            await context.Response.WriteAsync($"data: {data}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }

        if (done)
        {
            await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }

    public static Task TextAsync(HttpContext context, params string[] pieces) =>
        StreamAsync(context, pieces.Select(piece => Chunk(piece)).Append(Chunk(null, "stop")));

    public static Task Status(HttpContext context, int status, string body)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsync(body, context.RequestAborted);
    }

    public static string Chunk(string? content, string? finishReason = null) =>
        JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content }, finish_reason = finishReason } } });

    public static string Usage(int prompt, int completion, int? cached = null) =>
        JsonSerializer.Serialize(new
        {
            choices = Array.Empty<object>(),
            usage = new { prompt_tokens = prompt, completion_tokens = completion, prompt_tokens_details = cached is null ? null : new { cached_tokens = cached } },
        });
}
