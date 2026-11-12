using System.Text.Json;
using LivingPaper.Application;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace LivingPaper.Infrastructure.Tests;

// The list of models of an OpenAI-compatible provider (GET {baseUrl}/models), against a fake provider.
public sealed class OpenAiCompatibleModelCatalogTests : IAsyncLifetime
{
    private const string Key = "sk-catalog-secret-key";

    private FakeLlmServer _server = null!;
    private OpenAiCompatibleModelCatalog _catalog = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server = await FakeLlmServer.StartAsync();
        _catalog = new OpenAiCompatibleModelCatalog();
    }

    public async ValueTask DisposeAsync()
    {
        _catalog.Dispose();
        await _server.DisposeAsync();
    }

    private static Func<HttpContext, Task> Models(params string[] ids) => context =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { @object = "list", data = ids.Select(id => new { id, @object = "model" }) }), context.RequestAborted);
    };

    [Fact]
    public async Task List_ReadsTheIdsOfTheModels_AndSendsTheKeyAsABearerToken()
    {
        _server.Handler = Models("gpt-5", "gpt-5-nano");

        var result = await _catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Success.ShouldBeTrue();
        result.Models.ShouldBe(["gpt-5", "gpt-5-nano"]);
        var request = _server.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("GET");
        request.Path.ShouldBe("/v1/models");
        request.Authorization.ShouldBe("Bearer " + Key);
    }

    [Fact]
    public async Task List_SendsNoAuthorization_WhenThereIsNoKey_ForALocalServer()
    {
        _server.Handler = Models("llama3");

        var result = await _catalog.ListAsync(_server.BaseUrl + "/", string.Empty, Token);

        result.Models.ShouldBe(["llama3"]);
        _server.Requests.ShouldHaveSingleItem().Authorization.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_IgnoresEntriesWithoutAUsableId_AndRepeatedOnes()
    {
        _server.Handler = context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("""{"data":[{"id":"a"},{"id":""},{"name":"no-id"},{"id":7},{"id":"a"},{"id":"bad\nid"},{"id":"b"}]}""", context.RequestAborted);
        };

        var result = await _catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Models.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task List_KeepsAtMostAFewHundredModels()
    {
        _server.Handler = Models([.. Enumerable.Range(0, 800).Select(number => $"model-{number:D4}")]);

        var result = await _catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Models.Count.ShouldBe(OpenAiCompatibleModelCatalog.MaxModels);
    }

    [Theory]
    [InlineData(401, ConnectionFailure.KeyRefused)]
    [InlineData(403, ConnectionFailure.KeyRefused)]
    [InlineData(404, ConnectionFailure.NoModelList)]
    [InlineData(405, ConnectionFailure.NoModelList)]
    [InlineData(501, ConnectionFailure.NoModelList)]
    [InlineData(429, ConnectionFailure.ProviderError)]
    [InlineData(500, ConnectionFailure.ProviderError)]
    [InlineData(503, ConnectionFailure.ProviderError)]
    public async Task List_SortsTheHttpErrors_ByWhatTheUserCanDo(int status, ConnectionFailure expected)
    {
        _server.Handler = context => FakeReplies.Status(context, status, "{\"error\":\"echo " + Key + "\"}");

        var result = await _catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Failure.ShouldBe(expected);
        result.HttpStatus.ShouldBe(status);
        result.Models.ShouldBeEmpty();
        result.ToString().ShouldNotContain(Key); // The body of an error can echo the key: it is never read.
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"data":"nope"}""")]
    [InlineData("""{"models":[{"id":"a"}]}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("[1,2,3]")]
    public async Task List_SaysThereIsNoList_WhenTheAnswerIsNotAListOfModels(string body)
    {
        _server.Handler = context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(body, context.RequestAborted);
        };

        var result = await _catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Failure.ShouldBe(ConnectionFailure.NoModelList);
    }

    [Fact]
    public async Task List_IsUnreachable_WhenNothingListensAtTheAddress()
    {
        var address = _server.BaseUrl;
        await _server.DisposeAsync();
        _server = await FakeLlmServer.StartAsync(); // So that DisposeAsync of the test has something to stop.

        var result = await _catalog.ListAsync(address, Key, Token);

        result.Failure.ShouldBe(ConnectionFailure.Unreachable);
    }

    [Fact]
    public async Task List_DoesNotFollowRedirects_SoTheKeyIsNeverSentElsewhere()
    {
        _server.Handler = context =>
        {
            context.Response.StatusCode = 307;
            context.Response.Headers.Location = "/elsewhere/v1/models";
            return Task.CompletedTask;
        };

        var result = await _catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Failure.ShouldBe(ConnectionFailure.ProviderError);
        result.HttpStatus.ShouldBe(307);
        _server.Requests.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("ftp://example.com/v1")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task List_RefusesAnAddressThatIsNotHttp(string baseUrl)
    {
        var result = await _catalog.ListAsync(baseUrl, Key, Token);

        result.Failure.ShouldBe(ConnectionFailure.Unreachable);
    }

    [Fact]
    public async Task List_RefusesAKeyWithAControlCharacter_InsteadOfBreakingTheHeader()
    {
        var result = await _catalog.ListAsync(_server.BaseUrl, "sk-bad\nkey", Token);

        result.Failure.ShouldBe(ConnectionFailure.Unreachable);
        _server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_GivesUp_WhenTheProviderDoesNotAnswerInTime()
    {
        using var catalog = new OpenAiCompatibleModelCatalog(timeout: TimeSpan.FromMilliseconds(300));
        _server.Handler = async context => await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);

        var result = await catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Failure.ShouldBe(ConnectionFailure.Unreachable);
    }

    [Fact]
    public async Task List_StopsReadingAnAnswerThatIsTooBig()
    {
        _server.Handler = async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"data\":[{\"id\":\"a\"}],\"padding\":\"", context.RequestAborted);
            var chunk = new string('x', 64 * 1024);
            for (var count = 0; count < 64; count++)
            {
                await context.Response.WriteAsync(chunk, context.RequestAborted);
            }

            await context.Response.WriteAsync("\"}", context.RequestAborted);
        };

        var result = await _catalog.ListAsync(_server.BaseUrl, Key, Token);

        result.Failure.ShouldBe(ConnectionFailure.NoModelList);
    }
}
