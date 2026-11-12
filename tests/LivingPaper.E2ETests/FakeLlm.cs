using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LivingPaper.E2ETests;

// A real HTTP server (Kestrel, on a free local port) that plays an OpenAI-compatible provider, so the application under test
// makes real requests and no request ever leaves the machine. It tells what is asked from the text of the prompt: the prompts
// are those of data/prompts.json (the defaults), and the answers are the JSON that the analysis asks for.
internal sealed class FakeLlm : IAsyncDisposable
{
    public const string FirstPiece = "Good evening, ";

    public const string LastPiece = "traveller. Sit by the fire.";

    public const string Reply = FirstPiece + LastPiece;

    private static readonly string[] Maggie = ["Maggie"];
    private static readonly string[] NoOtherName = [];

    private readonly WebApplication _app;
    private readonly List<string> _bodies = [];
    private TaskCompletionSource _chatGate = Opened();
    private TaskCompletionSource _analysisGate = Opened();

    private FakeLlm(WebApplication app, string address)
    {
        _app = app;
        BaseUrl = address + "/v1";
    }

    // What a user would type in the field "Address" of the settings.
    public string BaseUrl { get; }

    // The provider refuses every request, as with a wrong key (HTTP 401).
    public bool RefuseEverything { get; set; }

    // The provider refuses the chat only (the connection test and the analysis still work).
    public bool RefuseTheChat { get; set; }

    public int ChatRequests { get; private set; }

    public IReadOnlyList<string> Bodies
    {
        get
        {
            lock (_bodies)
            {
                return [.. _bodies];
            }
        }
    }

    public static async Task<FakeLlm> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        FakeLlm? server = null;
        app.MapPost("/v1/chat/completions", context => server!.HandleAsync(context));
        app.MapGet("/v1/models", context => server!.ListModelsAsync(context));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server = new FakeLlm(app, address);
        return server;
    }

    // The reply of the chat stops after its first piece until Release is called: a test can see the reply half written.
    public void HoldTheChatAfterTheFirstPiece() => _chatGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    // The analysis waits (its first request to read the book gets no answer) until Release is called: a test can see it running.
    public void HoldTheAnalysis() => _analysisGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release()
    {
        _chatGate.TrySetResult();
        _analysisGate.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Release();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static TaskCompletionSource Opened()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return gate;
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        lock (_bodies)
        {
            _bodies.Add(body);
        }

        var isChat = IsChat(body);
        if (isChat)
        {
            ChatRequests++;
        }

        if (RefuseEverything || (isChat && RefuseTheChat))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("""{"error":{"message":"Incorrect API key provided."}}""", context.RequestAborted);
            return;
        }

        context.Response.ContentType = "text/event-stream";
        await context.Response.StartAsync(context.RequestAborted);
        if (isChat)
        {
            await SendAsync(context, Chunk(FirstPiece));
            await _chatGate.Task.WaitAsync(context.RequestAborted);
            await SendAsync(context, Chunk(LastPiece));
        }
        else
        {
            await _analysisGate.Task.WaitAsync(context.RequestAborted);
            await SendAsync(context, Chunk(Answer(body)));
        }

        await SendAsync(context, Chunk(null, "stop"));
        await SendAsync(context, Usage(120, 40));
        await SendAsync(context, "[DONE]");
    }

    // The list of models the settings page asks for before the user chooses one.
    private Task ListModelsAsync(HttpContext context)
    {
        if (RefuseEverything)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsync("""{"error":{"message":"Incorrect API key provided."}}""", context.RequestAborted);
        }

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync("""{"object":"list","data":[{"id":"other-model","object":"model"},{"id":"fake-model","object":"model"}]}""", context.RequestAborted);
    }

    // The chat is the only request whose first message is the system prompt that starts "You play".
    private static bool IsChat(string body) => body.Contains("You play ", StringComparison.Ordinal);

    private static string Answer(string body)
    {
        if (body.Contains("Reply with the single word", StringComparison.Ordinal))
        {
            return "pong";
        }

        if (body.Contains("You are reading an excerpt", StringComparison.Ordinal))
        {
            return Json(new
            {
                characters = new object[]
                {
                    new { name = "Marguerite Voss", otherNames = Maggie, role = "The keeper of the lighthouse.", personality = "Stubborn, tender, afraid of the sea.", speech = "Short sentences, dry humour.", events = "She writes to her brother and keeps the light on.", importance = 9 },
                    new { name = "Captain Alder", otherNames = NoOtherName, role = "A retired sailor who brings the letters.", personality = "Gentle, talkative, proud of his past.", speech = "Sea words, long stories.", events = "He rows to the island each week.", importance = 6 },
                },
            });
        }

        if (body.Contains("Keep the ", StringComparison.Ordinal) && body.Contains("most important characters", StringComparison.Ordinal))
        {
            return Json(new
            {
                characters = new object[]
                {
                    new { name = "Marguerite Voss", shortDescription = "The stubborn keeper of a lighthouse who writes to her brother." },
                    new { name = "Captain Alder", shortDescription = "A retired sailor who rows to the island with the letters." },
                },
            });
        }

        if (body.Contains("Write the chat profile", StringComparison.Ordinal))
        {
            return "Marguerite speaks in short, dry sentences and hides her fear of the sea behind jokes. She keeps the light of the lighthouse, "
                + "writes every week to her brother, and trusts only Captain Alder, who brings the letters. She knows the whole story, to the last storm.";
        }

        if (body.Contains("summary of the atmosphere", StringComparison.Ordinal))
        {
            return "A lonely island at the end of the nineteenth century: gentle, melancholic, salt and lamp oil.";
        }

        return "ok";
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string Chunk(string? content, string? finishReason = null) =>
        JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content }, finish_reason = finishReason } } });

    private static string Usage(int prompt, int completion) =>
        JsonSerializer.Serialize(new { choices = Array.Empty<object>(), usage = new { prompt_tokens = prompt, completion_tokens = completion } });

    private static async Task SendAsync(HttpContext context, string data)
    {
        await context.Response.WriteAsync($"data: {data}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
}
