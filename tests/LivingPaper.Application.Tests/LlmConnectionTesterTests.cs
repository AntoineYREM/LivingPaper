using System.Diagnostics;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

public class LlmConnectionTesterTests
{
    private static readonly LlmProvider Provider = new("id", "OpenAI", "https://api.openai.com/v1", "sk-test-key", "gpt-5-nano");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LlmConnectionTester NewTester(FakeChatModelFactory factory, FakePromptProvider? prompts = null, TimeSpan? timeout = null) =>
        new(factory, new FakeEmbeddingModelFactory(), prompts ?? new FakePromptProvider(), timeout);

    private static LlmConnectionTester NewEmbeddingTester(FakeEmbeddingModelFactory embeddings, FakePromptProvider? prompts = null, TimeSpan? timeout = null) =>
        new(new FakeChatModelFactory(), embeddings, prompts ?? new FakePromptProvider(), timeout);

    private static ChatProviderException Http(int status) => new(ChatFailureKind.Http, $"The language model answered HTTP {status}.", status);

    [Fact]
    public async Task Test_Succeeds_WhenTheModelAnswers()
    {
        var factory = new FakeChatModelFactory();

        var result = await NewTester(factory).TestAsync(Provider, Token);

        result.Success.ShouldBeTrue();
        result.Failure.ShouldBeNull();
        result.HttpStatus.ShouldBeNull();
    }

    [Fact]
    public async Task Test_SendsTheConnectionTestPromptOfThePromptsFile_AsTheOnlyMessage()
    {
        var factory = new FakeChatModelFactory();
        var prompts = new FakePromptProvider("Say pong, please.");

        await NewTester(factory, prompts).TestAsync(Provider, Token);

        prompts.Requests.ShouldHaveSingleItem().Key.ShouldBe(PromptKeys.ConnectionTest);
        prompts.Requests[0].Variables.ShouldBeEmpty();
        factory.Sent.ShouldHaveSingleItem().ShouldBe([new ChatMessage(ChatRole.User, "Say pong, please.")]);
    }

    [Fact]
    public async Task Test_RunsTheCallInsideItsOwnTrace_SoItIsNotMistakenForAnAnalysis()
    {
        string? span = null;
        var factory = new FakeChatModelFactory
        {
            Reply = (_, _) =>
            {
                span = Activity.Current?.OperationName;
                return Task.FromResult("pong");
            },
        };
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LlmTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        await NewTester(factory).TestAsync(Provider, Token);

        span.ShouldBe("connection-test");
    }

    [Fact]
    public async Task Test_AsksForAClientWithoutRetries_ForTheProviderBeingTested()
    {
        var factory = new FakeChatModelFactory();

        await NewTester(factory).TestAsync(Provider, Token);

        factory.Created.ShouldHaveSingleItem().ShouldBe((Provider, false));
    }

    [Theory]
    [InlineData(ChatFailureKind.Unreachable, ConnectionFailure.Unreachable)]
    [InlineData(ChatFailureKind.TimedOut, ConnectionFailure.Unreachable)]
    [InlineData(ChatFailureKind.InvalidResponse, ConnectionFailure.InvalidResponse)]
    [InlineData(ChatFailureKind.EmptyReply, ConnectionFailure.InvalidResponse)]
    [InlineData(ChatFailureKind.StreamCut, ConnectionFailure.InvalidResponse)]
    [InlineData(ChatFailureKind.ContentFiltered, ConnectionFailure.InvalidResponse)]
    public async Task Test_TellsWhatWentWrong_ForAFailureThatIsNotAnHttpStatus(ChatFailureKind kind, ConnectionFailure expected)
    {
        var factory = new FakeChatModelFactory { Reply = (_, _) => throw new ChatProviderException(kind, "failure") };

        var result = await NewTester(factory).TestAsync(Provider, Token);

        result.Success.ShouldBeFalse();
        result.Failure.ShouldBe(expected);
        result.HttpStatus.ShouldBeNull();
    }

    [Theory]
    [InlineData(401, ConnectionFailure.KeyRefused)]
    [InlineData(403, ConnectionFailure.KeyRefused)]
    [InlineData(404, ConnectionFailure.ModelUnknown)]
    [InlineData(400, ConnectionFailure.ModelUnknown)]
    [InlineData(429, ConnectionFailure.ProviderError)]
    [InlineData(500, ConnectionFailure.ProviderError)]
    [InlineData(503, ConnectionFailure.ProviderError)]
    [InlineData(302, ConnectionFailure.ProviderError)]
    [InlineData(418, ConnectionFailure.ProviderError)]
    public async Task Test_TellsWhatWentWrong_ForAnHttpStatus_AndKeepsTheStatus(int status, ConnectionFailure expected)
    {
        var factory = new FakeChatModelFactory { Reply = (_, _) => throw Http(status) };

        var result = await NewTester(factory).TestAsync(Provider, Token);

        result.Success.ShouldBeFalse();
        result.Failure.ShouldBe(expected);
        result.HttpStatus.ShouldBe(status);
    }

    [Fact]
    public async Task Test_GivesUp_WhenTheModelDoesNotAnswerInTime_ReportedAsUnreachable()
    {
        var factory = new FakeChatModelFactory { Reply = async (_, cancellationToken) => { await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); return "late"; } };

        var result = await NewTester(factory, timeout: TimeSpan.FromMilliseconds(150)).TestAsync(Provider, Token);

        result.Failure.ShouldBe(ConnectionFailure.Unreachable);
    }

    [Fact]
    public async Task Test_StaysCancelled_WhenTheCallerCancels()
    {
        var factory = new FakeChatModelFactory { Reply = async (_, cancellationToken) => { await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); return "late"; } };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => NewTester(factory).TestAsync(Provider, cancellation.Token));
    }

    [Fact]
    public async Task Test_ReportsAnInvalidPromptsFile_WithoutCallingTheModel()
    {
        var factory = new FakeChatModelFactory();
        var prompts = new FakePromptProvider(failure: new PromptFileException("prompts.json", PromptKeys.ConnectionTest, "the prompt is empty"));

        var result = await NewTester(factory, prompts).TestAsync(Provider, Token);

        result.Failure.ShouldBe(ConnectionFailure.PromptFile);
        result.Detail.ShouldNotBeNull().ShouldContain("prompts.json");
        factory.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Test_ReportsAnAddressTheClientRefuses_AsUnreachable()
    {
        var factory = new FakeChatModelFactory { CreateFailure = new ArgumentException("The base URL of the language model must be an absolute http or https URL.") };

        var result = await NewTester(factory).TestAsync(Provider, Token);

        result.Failure.ShouldBe(ConnectionFailure.Unreachable);
    }

    [Fact]
    public async Task Test_NeverPutsTheKeyOrTheAddressInTheResult()
    {
        var factory = new FakeChatModelFactory { Reply = (_, _) => throw Http(401) };

        var result = await NewTester(factory).TestAsync(Provider, Token);

        var text = result.ToString() + result.Detail;
        text.ShouldNotContain("sk-test-key");
        text.ShouldNotContain("api.openai.com");
    }

    // ---- The embedding model ----

    [Fact]
    public async Task TestEmbedding_VectorizesTheTextOfTheConnectionTest_OnceAndWithoutRetries()
    {
        var embeddings = new FakeEmbeddingModelFactory();
        var prompts = new FakePromptProvider("Say pong, please.");

        var result = await NewEmbeddingTester(embeddings, prompts).TestEmbeddingAsync(Provider, Token);

        result.Success.ShouldBeTrue();
        prompts.Requests.ShouldHaveSingleItem().Key.ShouldBe(PromptKeys.ConnectionTest);
        embeddings.Created.ShouldHaveSingleItem().ShouldBe((Provider, false));
        embeddings.Sent.ShouldHaveSingleItem().ShouldBe(["Say pong, please."]);
    }

    [Theory]
    [InlineData(400, ConnectionFailure.ModelUnknown)]
    [InlineData(401, ConnectionFailure.KeyRefused)]
    [InlineData(503, ConnectionFailure.ProviderError)]
    public async Task TestEmbedding_TellsWhatWentWrong_ForAnHttpStatus(int status, ConnectionFailure expected)
    {
        var embeddings = new FakeEmbeddingModelFactory { Reply = (_, _) => throw Http(status) };

        var result = await NewEmbeddingTester(embeddings).TestEmbeddingAsync(Provider, Token);

        result.Failure.ShouldBe(expected);
        result.HttpStatus.ShouldBe(status);
    }

    [Fact]
    public async Task TestEmbedding_GivesUp_WhenTheModelDoesNotAnswerInTime()
    {
        var embeddings = new FakeEmbeddingModelFactory
        {
            Reply = async (_, cancellationToken) => { await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); return []; },
        };

        var result = await NewEmbeddingTester(embeddings, timeout: TimeSpan.FromMilliseconds(150)).TestEmbeddingAsync(Provider, Token);

        result.Failure.ShouldBe(ConnectionFailure.Unreachable);
    }

    [Fact]
    public async Task TestEmbedding_ReportsAnInvalidPromptsFile_AndAnAddressTheClientRefuses()
    {
        var embeddings = new FakeEmbeddingModelFactory();
        var prompts = new FakePromptProvider(failure: new PromptFileException("prompts.json", PromptKeys.ConnectionTest, "the prompt is empty"));
        var refusing = new FakeEmbeddingModelFactory { CreateFailure = new ArgumentException("bad address") };

        (await NewEmbeddingTester(embeddings, prompts).TestEmbeddingAsync(Provider, Token)).Failure.ShouldBe(ConnectionFailure.PromptFile);
        (await NewEmbeddingTester(refusing).TestEmbeddingAsync(Provider, Token)).Failure.ShouldBe(ConnectionFailure.Unreachable);
        embeddings.Created.ShouldBeEmpty();
    }

    // ---- A refusal, as opposed to a slow or failing provider ----

    [Theory]
    [InlineData(400, true)]
    [InlineData(401, true)]
    [InlineData(404, true)]
    [InlineData(405, true)]
    [InlineData(422, true)]
    [InlineData(501, true)]
    [InlineData(408, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    [InlineData(302, false)]
    [InlineData(null, false)]
    public void ARefusal_IsAnHttpAnswerThatWouldBeTheSameAgain(int? status, bool expected)
    {
        ConnectionTestResult.Failed(ConnectionFailure.ProviderError, status).IsRefusal.ShouldBe(expected);
    }

    [Fact]
    public void ASuccess_IsNotARefusal() => ConnectionTestResult.Ok().IsRefusal.ShouldBeFalse();

    [Fact]
    public void OkAndFailed_BuildCoherentResults()
    {
        ConnectionTestResult.Ok().Success.ShouldBeTrue();
        var failed = ConnectionTestResult.Failed(ConnectionFailure.KeyRefused, 401);
        failed.Success.ShouldBeFalse();
        failed.Failure.ShouldBe(ConnectionFailure.KeyRefused);
        failed.HttpStatus.ShouldBe(401);
    }
}
