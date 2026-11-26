using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

public sealed class LlmSettingsServiceTests : IDisposable
{
    private const string Key = "sk-very-secret-key";

    private readonly InMemoryLlmSettingsRepository _repository = new();
    private readonly FakeConnectionTester _tester;
    private readonly LlmSettingsService _service;

    public LlmSettingsServiceTests()
    {
        _tester = new FakeConnectionTester();
        _service = new LlmSettingsService(_repository, _tester, new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));
    }

    public void Dispose() => _service.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LlmProviderDraft Draft(string name = "OpenAI", string key = Key) => new(name, "https://api.openai.com/v1", key, "gpt-5-nano");

    private static LlmProvider Provider(string id, string name, string key = "key") => new(id, name, "https://example.com/v1", key, "model");

    private LlmSettingsService ServiceWith(FakeConnectionTester tester) => new(_repository, tester, new FakeModelCatalog(), new PendingProviderStore(TimeProvider.System));

    // ---- Adding ----

    [Fact]
    public async Task Add_TestsTheConnection_ThenSavesAndChoosesTheFirstModelForBothUses()
    {
        var result = await _service.AddAsync(new LlmProviderDraft("  OpenAI  ", " https://api.openai.com/v1 ", $"  {Key}\n", " gpt-5-nano "), Token);

        result.Succeeded.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        result.Added.ShouldNotBeNull();
        result.Added.Name.ShouldBe("OpenAI");
        result.Added.BaseUrl.ShouldBe("https://api.openai.com/v1");
        result.Added.Model.ShouldBe("gpt-5-nano");
        result.Added.HasKey.ShouldBeTrue();
        result.Added.KeyUnreadable.ShouldBeFalse();
        BookIdLike(result.Added.Id).ShouldBeTrue();

        var tested = _tester.Tested.ShouldHaveSingleItem();
        tested.Id.ShouldBe(result.Added.Id);
        tested.Name.ShouldBe("OpenAI"); // The fields are trimmed before the test and the save.
        tested.ApiKey.ShouldBe(Key);
        tested.Model.ShouldBe("gpt-5-nano");

        _repository.Saves.ShouldBe(1);
        var view = await _service.GetAsync(Token);
        view.AnalysisProviderId.ShouldBe(result.Added.Id);
        view.ChatProviderId.ShouldBe(result.Added.Id);
        view.IsAnalysisEnabled.ShouldBeTrue();
        view.IsChatEnabled.ShouldBeTrue();
    }

    private static bool BookIdLike(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigitLower);

    [Fact]
    public async Task Add_AcceptsAModelWithoutKey_ForALocalServer()
    {
        var result = await _service.AddAsync(Draft(key: string.Empty), Token);

        result.Succeeded.ShouldBeTrue();
        result.Added!.HasKey.ShouldBeFalse();
        _tester.Tested.Single().ApiKey.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_KeepsTheChoicesAlreadyMade_ForASecondModel()
    {
        var first = (await _service.AddAsync(Draft("First"), Token)).Added!;

        var second = (await _service.AddAsync(Draft("Second"), Token)).Added!;

        var view = await _service.GetAsync(Token);
        view.Providers.Select(provider => provider.Id).ShouldBe([first.Id, second.Id]);
        view.AnalysisProviderId.ShouldBe(first.Id);
        view.ChatProviderId.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Add_RefusesAnInvalidModel_WithoutTestingOrSaving()
    {
        var result = await _service.AddAsync(new LlmProviderDraft(string.Empty, "ftp://example.com", Key, string.Empty), Token);

        result.Succeeded.ShouldBeFalse();
        result.Connection.ShouldBeNull();
        result.Errors.ShouldBe(
        [
            new FieldError("name", LlmProviderErrors.Required),
            new FieldError("baseUrl", LlmProviderErrors.Scheme),
            new FieldError("model", LlmProviderErrors.Required),
        ]);
        _tester.Tested.ShouldBeEmpty();
        _repository.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Add_RefusesANameAlreadyUsed_IgnoringCase()
    {
        await _service.AddAsync(Draft("OpenAI"), Token);
        _tester.Tested.Clear();

        var result = await _service.AddAsync(Draft("  openai "), Token);

        result.Errors.ShouldBe([new FieldError("name", LlmProviderErrors.Taken)]);
        _tester.Tested.ShouldBeEmpty();
        _repository.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Add_RefusesAModelWhenThereAreAlreadyTooMany()
    {
        _repository.Seed(LlmSettings.Restore(Enumerable.Range(0, LlmProviderLimits.MaxProviders).Select(index => Provider($"id{index}", $"Name {index}")), null, null));

        var result = await _service.AddAsync(Draft("One too many"), Token);

        result.Errors.ShouldBe([new FieldError(string.Empty, LlmProviderErrors.TooMany)]);
        _tester.Tested.ShouldBeEmpty();
        _repository.Saves.ShouldBe(0);
    }

    [Theory]
    [InlineData(ConnectionFailure.Unreachable, null)]
    [InlineData(ConnectionFailure.KeyRefused, 401)]
    [InlineData(ConnectionFailure.ModelUnknown, 404)]
    [InlineData(ConnectionFailure.InvalidResponse, null)]
    [InlineData(ConnectionFailure.ProviderError, 503)]
    [InlineData(ConnectionFailure.PromptFile, null)]
    public async Task Add_DoesNotSaveAModelWhoseTestFails_AndSaysWhy(ConnectionFailure failure, int? status)
    {
        var tester = Tester(ConnectionTestResult.Failed(failure, status), ConnectionTestResult.Failed(failure, status));
        using var service = ServiceWith(tester);

        var result = await service.AddAsync(Draft(), Token);

        result.Succeeded.ShouldBeFalse();
        result.Added.ShouldBeNull();
        result.Errors.ShouldBeEmpty();
        result.Connection.ShouldNotBeNull().Failure.ShouldBe(failure);
        result.Connection.HttpStatus.ShouldBe(status);
        _repository.Saves.ShouldBe(0);
        (await service.GetAsync(Token)).Providers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_NamesTheModelThatWasAddedByAnotherRequestMeanwhile_AndDoesNotSaveDuplicates()
    {
        // While the connection is being tested (it can take seconds), another request adds a model with the same name.
        var tester = new FakeConnectionTester(_ =>
        {
            _repository.Seed(LlmSettings.Restore([Provider("other", "OpenAI")], null, null));
            return Task.FromResult(ConnectionTestResult.Ok());
        });
        using var service = ServiceWith(tester);

        var result = await service.AddAsync(Draft("OpenAI"), Token);

        result.Errors.ShouldBe([new FieldError("name", LlmProviderErrors.Taken)]);
        result.Succeeded.ShouldBeFalse();
        _repository.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Add_KeepsBothModels_WhenTwoAreAddedAtTheSameTime()
    {
        var tester = new FakeConnectionTester(async _ =>
        {
            await Task.Delay(50, Token);
            return ConnectionTestResult.Ok();
        });
        using var service = ServiceWith(tester);

        var results = await Task.WhenAll(service.AddAsync(Draft("First"), Token), service.AddAsync(Draft("Second"), Token));

        results.ShouldAllBe(result => result.Succeeded);
        (await service.GetAsync(Token)).Providers.Select(provider => provider.Name).Order().ShouldBe(["First", "Second"]);
    }

    [Fact]
    public async Task Add_Fails_WhenTheSettingsFileCannotBeRead_InsteadOfReplacingIt()
    {
        _repository.LoadFailure = new SettingsFileException("settings.json", "the file is not valid JSON");

        await Should.ThrowAsync<SettingsFileException>(() => _service.AddAsync(Draft(), Token));

        _tester.Tested.ShouldBeEmpty();
        _repository.Saves.ShouldBe(0);
    }

    // ---- Deleting ----

    [Fact]
    public async Task Delete_RemovesTheModel_AndUnchoosesItWithoutChoosingAnotherInItsPlace()
    {
        var first = (await _service.AddAsync(Draft("First"), Token)).Added!;
        var second = (await _service.AddAsync(Draft("Second"), Token)).Added!;

        (await _service.DeleteAsync(first.Id, Token)).ShouldBeTrue();

        var view = await _service.GetAsync(Token);
        view.Providers.Select(provider => provider.Id).ShouldBe([second.Id]);
        view.AnalysisProviderId.ShouldBeNull();
        view.ChatProviderId.ShouldBeNull();
        view.IsAnalysisEnabled.ShouldBeFalse();
        view.IsChatEnabled.ShouldBeFalse();
        (await _service.GetAnalysisProviderAsync(Token)).ShouldBeNull();
        (await _service.GetChatProviderAsync(Token)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_ReturnsFalseAndSavesNothing_ForAnUnknownModel()
    {
        await _service.AddAsync(Draft(), Token);
        var saves = _repository.Saves;

        (await _service.DeleteAsync("unknown", Token)).ShouldBeFalse();

        _repository.Saves.ShouldBe(saves);
    }

    // ---- Choosing ----

    [Fact]
    public async Task Select_ChoosesADifferentModelForTheChat()
    {
        var first = (await _service.AddAsync(Draft("First"), Token)).Added!;
        var second = (await _service.AddAsync(Draft("Second"), Token)).Added!;

        (await _service.SelectForChatAsync(second.Id, Token)).ShouldBeTrue();

        var view = await _service.GetAsync(Token);
        view.AnalysisProviderId.ShouldBe(first.Id);
        view.ChatProviderId.ShouldBe(second.Id);
        (await _service.GetChatProviderAsync(Token))!.Id.ShouldBe(second.Id);
        (await _service.GetAnalysisProviderAsync(Token))!.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Select_AfterADeletion_EnablesAnalysisAgain()
    {
        var first = (await _service.AddAsync(Draft("First"), Token)).Added!;
        var second = (await _service.AddAsync(Draft("Second"), Token)).Added!;
        await _service.DeleteAsync(first.Id, Token);

        (await _service.SelectForAnalysisAsync(second.Id, Token)).ShouldBeTrue();

        var view = await _service.GetAsync(Token);
        view.IsAnalysisEnabled.ShouldBeTrue();
        view.IsChatEnabled.ShouldBeFalse(); // Only what the user chose is enabled.
    }

    [Fact]
    public async Task Select_Null_DisablesThatUse()
    {
        await _service.AddAsync(Draft(), Token);

        (await _service.SelectForAnalysisAsync(null, Token)).ShouldBeTrue();

        var view = await _service.GetAsync(Token);
        view.AnalysisProviderId.ShouldBeNull();
        view.IsAnalysisEnabled.ShouldBeFalse();
        view.IsChatEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Select_ReturnsFalseAndKeepsTheChoice_ForAnUnknownModel()
    {
        var first = (await _service.AddAsync(Draft(), Token)).Added!;
        var saves = _repository.Saves;

        (await _service.SelectForAnalysisAsync("unknown", Token)).ShouldBeFalse();
        (await _service.SelectForChatAsync("unknown", Token)).ShouldBeFalse();

        _repository.Saves.ShouldBe(saves);
        (await _service.GetAsync(Token)).AnalysisProviderId.ShouldBe(first.Id);
    }

    // ---- Adding a model that cannot chat ----

    private static ConnectionTestResult Refused { get; } = ConnectionTestResult.Failed(ConnectionFailure.ModelUnknown, 400);

    private static FakeConnectionTester Tester(ConnectionTestResult chat, ConnectionTestResult? embedding = null) =>
        new(_ => Task.FromResult(chat), _ => Task.FromResult(embedding ?? ConnectionTestResult.Ok()));

    [Fact]
    public async Task Add_TriesOnlyTheChat_WhenItAnswers()
    {
        await _service.AddAsync(Draft(), Token);

        _tester.Embedded.ShouldBeEmpty();
        (await _service.GetAsync(Token)).EmbeddingProviderId.ShouldBeNull();
    }

    [Fact]
    public async Task Add_ChoosesAFirstModelThatOnlyVectorizes_ForVectorizationAlone()
    {
        var tester = Tester(Refused);
        using var service = ServiceWith(tester);

        var result = await service.AddAsync(Draft("bge"), Token);

        result.Succeeded.ShouldBeTrue();
        result.Connection.ShouldNotBeNull().Success.ShouldBeTrue();
        tester.Embedded.ShouldHaveSingleItem().Name.ShouldBe("bge");
        var view = await service.GetAsync(Token);
        view.EmbeddingProviderId.ShouldBe(result.Added!.Id);
        view.AnalysisProviderId.ShouldBeNull();
        view.ChatProviderId.ShouldBeNull();
    }

    [Theory]
    [InlineData(ConnectionFailure.Unreachable, null)]
    [InlineData(ConnectionFailure.ProviderError, 503)]
    [InlineData(ConnectionFailure.InvalidResponse, null)]
    public async Task Add_KeepsTheUsualChoices_ForAFirstModelWhoseChatFailsWithoutRefusing_ButWhichVectorizes(ConnectionFailure failure, int? status)
    {
        using var service = ServiceWith(Tester(ConnectionTestResult.Failed(failure, status)));

        var result = await service.AddAsync(Draft(), Token);

        result.Succeeded.ShouldBeTrue(); // One of the two tries is enough to save it.
        var view = await service.GetAsync(Token);
        view.ChatProviderId.ShouldBe(result.Added!.Id);
        view.AnalysisProviderId.ShouldBe(result.Added.Id);
        view.EmbeddingProviderId.ShouldBeNull(); // A slow model is not taken for an embedding model.
    }

    [Fact]
    public async Task Add_ChoosesALaterModelThatOnlyVectorizes_ForTheEmptyChoiceOfVectorization()
    {
        var tester = new FakeConnectionTester(provider => Task.FromResult(provider.Name == "bge" ? Refused : ConnectionTestResult.Ok()));
        using var service = ServiceWith(tester);
        var first = (await service.AddAsync(Draft("Chat"), Token)).Added!;

        var result = await service.AddAsync(Draft("bge"), Token);

        result.Succeeded.ShouldBeTrue();
        var view = await service.GetAsync(Token);
        view.EmbeddingProviderId.ShouldBe(result.Added!.Id);
        view.ChatProviderId.ShouldBe(first.Id);
        view.AnalysisProviderId.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Add_ReportsTheFailureOfTheChat_WhenNeitherTrySucceeds()
    {
        var tester = Tester(Refused, ConnectionTestResult.Failed(ConnectionFailure.KeyRefused, 401));
        using var service = ServiceWith(tester);

        var result = await service.AddAsync(Draft(), Token);

        result.Succeeded.ShouldBeFalse();
        result.Connection.ShouldBe(Refused);
        tester.Embedded.Count.ShouldBe(1);
        _repository.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Add_DoesNotTryToVectorize_WhenThePromptsFileCannotBeUsed()
    {
        var tester = Tester(ConnectionTestResult.Failed(ConnectionFailure.PromptFile, detail: "prompts.json"));
        using var service = ServiceWith(tester);

        var result = await service.AddAsync(Draft(), Token);

        result.Succeeded.ShouldBeFalse();
        tester.Embedded.ShouldBeEmpty();
    }

    // ---- Choosing the model for vectorization ----

    [Fact]
    public async Task SelectForEmbedding_TriesTheModel_ThenChoosesIt()
    {
        await _service.AddAsync(Draft("Chat"), Token);
        var second = (await _service.AddAsync(Draft("bge"), Token)).Added!;

        var result = await _service.SelectForEmbeddingAsync(second.Id, Token);

        result.ShouldNotBeNull().Success.ShouldBeTrue();
        _tester.Embedded.ShouldHaveSingleItem().Id.ShouldBe(second.Id);
        (await _service.GetAsync(Token)).EmbeddingProviderId.ShouldBe(second.Id);
        (await _service.GetEmbeddingProviderAsync(Token))!.ApiKey.ShouldBe(Key);
    }

    [Fact]
    public async Task SelectForEmbedding_KeepsThePreviousChoice_WhenTheModelCannotVectorize()
    {
        var tester = new FakeConnectionTester(embeddingTest: provider => Task.FromResult(provider.Name == "bge" ? ConnectionTestResult.Ok() : Refused));
        using var service = ServiceWith(tester);
        var chat = (await service.AddAsync(Draft("Chat"), Token)).Added!;
        var bge = (await service.AddAsync(Draft("bge"), Token)).Added!;
        await service.SelectForEmbeddingAsync(bge.Id, Token);
        var saves = _repository.Saves;

        var result = await service.SelectForEmbeddingAsync(chat.Id, Token);

        result.ShouldBe(Refused);
        _repository.Saves.ShouldBe(saves);
        (await service.GetAsync(Token)).EmbeddingProviderId.ShouldBe(bge.Id);
    }

    [Fact]
    public async Task SelectForEmbedding_DoesNotTryAgainTheModelAlreadyChosen_NorTheChoiceOfNone()
    {
        await _service.AddAsync(Draft("Chat"), Token);
        var bge = (await _service.AddAsync(Draft("bge"), Token)).Added!;
        await _service.SelectForEmbeddingAsync(bge.Id, Token);
        _tester.Embedded.Clear();

        (await _service.SelectForEmbeddingAsync(bge.Id, Token))!.Success.ShouldBeTrue();
        (await _service.SelectForEmbeddingAsync(null, Token))!.Success.ShouldBeTrue();

        _tester.Embedded.ShouldBeEmpty();
        (await _service.GetAsync(Token)).EmbeddingProviderId.ShouldBeNull();
        (await _service.GetEmbeddingProviderAsync(Token)).ShouldBeNull();
    }

    [Fact]
    public async Task SelectForEmbedding_ReturnsNull_ForAnUnknownModel_WithoutTryingAnything()
    {
        await _service.AddAsync(Draft(), Token);
        var saves = _repository.Saves;

        (await _service.SelectForEmbeddingAsync("unknown", Token)).ShouldBeNull();

        _tester.Embedded.ShouldBeEmpty();
        _repository.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task SelectForEmbedding_ReturnsNull_WhenTheModelIsDeletedWhileItIsTried()
    {
        _repository.Seed(LlmSettings.Restore([Provider("a", "A")], null, null));
        var tester = new FakeConnectionTester(embeddingTest: _ =>
        {
            _repository.Seed(new LlmSettings());
            return Task.FromResult(ConnectionTestResult.Ok());
        });
        using var service = ServiceWith(tester);

        (await service.SelectForEmbeddingAsync("a", Token)).ShouldBeNull();

        (await service.GetAsync(Token)).EmbeddingProviderId.ShouldBeNull();
    }

    // ---- Reading ----

    [Fact]
    public async Task Get_NeverContainsAKey_NoMatterHowItIsRead()
    {
        await _service.AddAsync(Draft(), Token);

        var view = await _service.GetAsync(Token);

        JsonSerializer.Serialize(view).ShouldNotContain(Key);
        view.ToString().ShouldNotContain(Key);
        view.Providers.ShouldAllBe(provider => !provider.ToString()!.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_ReportsAModelWhoseKeyCannotBeRead_AndDoesNotEnableIt()
    {
        _repository.Seed(LlmSettings.Restore([Provider("a", "Lost key", key: string.Empty)], "a", "a", ["a"]));

        var view = await _service.GetAsync(Token);

        var summary = view.Providers.ShouldHaveSingleItem();
        summary.KeyUnreadable.ShouldBeTrue();
        summary.HasKey.ShouldBeFalse();
        view.AnalysisProviderId.ShouldBe("a");
        view.IsAnalysisEnabled.ShouldBeFalse();
        view.IsChatEnabled.ShouldBeFalse();
        (await _service.GetAnalysisProviderAsync(Token)).ShouldBeNull(); // Never used with an empty key.
    }

    [Fact]
    public async Task GetAnalysisProvider_GivesTheModelWithItsKey_ForTheServerToUse()
    {
        await _service.AddAsync(Draft(), Token);

        var provider = await _service.GetAnalysisProviderAsync(Token);

        provider.ShouldNotBeNull();
        provider.ApiKey.ShouldBe(Key);
        provider.Model.ShouldBe("gpt-5-nano");
    }

    [Fact]
    public async Task Get_ReturnsAnEmptyViewWithBothUsesDisabled_WhenNothingWasConfigured()
    {
        var view = await _service.GetAsync(Token);

        view.Providers.ShouldBeEmpty();
        view.IsAnalysisEnabled.ShouldBeFalse();
        view.IsChatEnabled.ShouldBeFalse();
        (await _service.GetChatProviderAsync(Token)).ShouldBeNull();
    }

    [Fact]
    public async Task Get_Fails_WhenTheSettingsFileCannotBeRead()
    {
        _repository.LoadFailure = new SettingsFileException("settings.json", "unsupported schemaVersion 9");

        var exception = await Should.ThrowAsync<SettingsFileException>(() => _service.GetAsync(Token));

        exception.FileName.ShouldBe("settings.json");
    }
}
