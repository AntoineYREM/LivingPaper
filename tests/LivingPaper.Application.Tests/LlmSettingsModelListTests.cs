using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// Adding a language model by asking the provider which models it has (PAPER-26): the key is checked by the list, the user
// picks one model, and nothing else is tested. The key stays on the server between the two steps.
public sealed class LlmSettingsModelListTests : IDisposable
{
    private const string Key = "sk-very-secret-key";

    private readonly InMemoryLlmSettingsRepository _repository = new();
    private readonly FakeConnectionTester _tester = new();
    private readonly FakeModelCatalog _catalog;
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
    private readonly LlmSettingsService _service;

    public LlmSettingsModelListTests()
    {
        _catalog = new FakeModelCatalog();
        _service = NewService(_catalog);
    }

    public void Dispose() => _service.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LlmProviderDraft Draft(string name = "OpenAI", string key = Key, string baseUrl = "https://api.openai.com/v1") =>
        new(name, baseUrl, key, null);

    private LlmSettingsService NewService(FakeModelCatalog catalog) =>
        new(_repository, _tester, catalog, new PendingProviderStore(_clock));

    // ---- Step 1: get the models ----

    [Fact]
    public async Task FetchModels_AsksTheProvider_AndKeepsADraftWithTheModelsSorted()
    {
        var result = await _service.FetchModelsAsync(Draft(), Token);

        result.Succeeded.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        var pending = result.Pending.ShouldNotBeNull();
        pending.Name.ShouldBe("OpenAI");
        pending.BaseUrl.ShouldBe("https://api.openai.com/v1");
        pending.Models.ShouldBe(["gpt-4o", "gpt-5", "gpt-5-nano"]);
        pending.CanChooseFromList.ShouldBeTrue();
        _catalog.Asked.ShouldHaveSingleItem().ApiKey.ShouldBe(Key);
        _tester.Tested.ShouldBeEmpty(); // The list is the proof that the key works.
        _repository.Saves.ShouldBe(0); // Nothing is saved before the user chooses.
    }

    [Fact]
    public async Task FetchModels_DoesNotCallTheProvider_WhenAFieldIsWrong()
    {
        var result = await _service.FetchModelsAsync(new LlmProviderDraft("", "ftp://example.com", Key, null), Token);

        result.Succeeded.ShouldBeFalse();
        result.Errors.Select(error => error.Field).ShouldBe(["name", "baseUrl"]); // Not "model": it is chosen in the next step.
        _catalog.Asked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ConnectionFailure.KeyRefused, 401)]
    [InlineData(ConnectionFailure.Unreachable, null)]
    [InlineData(ConnectionFailure.ProviderError, 503)]
    public async Task FetchModels_ReportsWhyTheProviderRefused_AndKeepsNoDraft(ConnectionFailure failure, int? status)
    {
        using var service = NewService(new FakeModelCatalog((_, _) => ModelListResult.Failed(failure, status)));

        var result = await service.FetchModelsAsync(Draft(), Token);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBe(failure);
        result.HttpStatus.ShouldBe(status);
        result.Pending.ShouldBeNull();
    }

    [Fact]
    public async Task FetchModels_WhenTheProviderHasNoList_KeepsADraftToTypeTheModelByHand()
    {
        using var service = NewService(new FakeModelCatalog((_, _) => ModelListResult.Failed(ConnectionFailure.NoModelList, 404)));

        var result = await service.FetchModelsAsync(Draft(), Token);

        result.Succeeded.ShouldBeTrue();
        var pending = result.Pending.ShouldNotBeNull();
        pending.CanChooseFromList.ShouldBeFalse();
        pending.Models.ShouldBeEmpty();
    }

    [Fact]
    public async Task FetchModels_IsRefused_WhenTheMaximumNumberOfModelsIsReached()
    {
        for (var index = 0; index < LlmProviderLimits.MaxProviders; index++)
        {
            (await _service.AddAsync(new LlmProviderDraft($"Model {index}", "https://example.com/v1", "k", "m"), Token)).Succeeded.ShouldBeTrue();
        }

        var result = await _service.FetchModelsAsync(Draft(name: "One too many"), Token);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(LlmProviderErrors.TooMany);
        _catalog.Asked.ShouldBeEmpty();
    }

    // ---- Step 2: choose and save ----

    [Fact]
    public async Task AddFetched_SavesTheChosenModel_WithoutAnotherTest_AndChoosesItForBothUses()
    {
        var pending = (await _service.FetchModelsAsync(Draft(), Token)).Pending!;

        var result = await _service.AddFetchedAsync(pending.Id, "gpt-5", Token);

        result.ShouldNotBeNull().Succeeded.ShouldBeTrue();
        result.Added!.Model.ShouldBe("gpt-5");
        result.Added.HasKey.ShouldBeTrue();
        _tester.Tested.ShouldBeEmpty();
        _repository.Saves.ShouldBe(1);
        (await _service.GetChatProviderAsync(Token))!.ApiKey.ShouldBe(Key);
        _service.GetPending(pending.Id).ShouldBeNull(); // The draft, and its key, are gone.
    }

    [Fact]
    public async Task AddFetched_RefusesAModelThatWasNotInTheList()
    {
        var pending = (await _service.FetchModelsAsync(Draft(), Token)).Pending!;

        var result = await _service.AddFetchedAsync(pending.Id, "invented-model", Token);

        var refused = result.ShouldNotBeNull();
        refused.Succeeded.ShouldBeFalse();
        refused.Errors.ShouldHaveSingleItem().Field.ShouldBe("model");
        _repository.Saves.ShouldBe(0);
        _service.GetPending(pending.Id).ShouldNotBeNull(); // The user can choose again.
    }

    [Fact]
    public async Task AddFetched_RefusesAnEmptyChoice()
    {
        var pending = (await _service.FetchModelsAsync(Draft(), Token)).Pending!;

        var result = await _service.AddFetchedAsync(pending.Id, "  ", Token);

        result.ShouldNotBeNull().Errors.ShouldHaveSingleItem().Code.ShouldBe(LlmProviderErrors.Required);
    }

    [Fact]
    public async Task AddFetched_RefusesAName_ThatWasTakenInTheMeantime()
    {
        var pending = (await _service.FetchModelsAsync(Draft(), Token)).Pending!;
        await _service.AddAsync(new LlmProviderDraft("openai", "https://example.com/v1", "k", "m"), Token);

        var result = await _service.AddFetchedAsync(pending.Id, "gpt-5", Token);

        result.ShouldNotBeNull().Errors.ShouldHaveSingleItem().Field.ShouldBe("name");
    }

    [Fact]
    public async Task AddFetched_ReturnsNull_ForAnUnknownDraft()
    {
        (await _service.AddFetchedAsync("not-a-draft", "gpt-5", Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AddFetched_WithoutAList_TestsTheConnectionOfTheModelTypedByHand_BeforeSaving()
    {
        using var service = NewService(new FakeModelCatalog((_, _) => ModelListResult.Failed(ConnectionFailure.NoModelList, 404)));
        var pending = (await service.FetchModelsAsync(Draft(), Token)).Pending!;

        var result = await service.AddFetchedAsync(pending.Id, " my-local-model ", Token);

        result.ShouldNotBeNull().Succeeded.ShouldBeTrue();
        var tested = _tester.Tested.ShouldHaveSingleItem();
        tested.Model.ShouldBe("my-local-model");
        tested.ApiKey.ShouldBe(Key);
    }

    [Fact]
    public async Task AddFetched_WithoutAList_KeepsTheDraft_WhenTheTestFails()
    {
        var failing = new FakeConnectionTester(_ => Task.FromResult(ConnectionTestResult.Failed(ConnectionFailure.ModelUnknown, 404)));
        using var service = new LlmSettingsService(_repository, failing, new FakeModelCatalog((_, _) => ModelListResult.Failed(ConnectionFailure.NoModelList)), new PendingProviderStore(_clock));
        var pending = (await service.FetchModelsAsync(Draft(), Token)).Pending!;

        var result = await service.AddFetchedAsync(pending.Id, "nope", Token);

        var refused = result.ShouldNotBeNull();
        refused.Succeeded.ShouldBeFalse();
        refused.Connection!.Failure.ShouldBe(ConnectionFailure.ModelUnknown);
        _repository.Saves.ShouldBe(0);
        service.GetPending(pending.Id).ShouldNotBeNull();
    }

    // ---- The draft lives a short time, in memory ----

    [Fact]
    public async Task ADraft_ExpiresAfterAFewMinutes()
    {
        var pending = (await _service.FetchModelsAsync(Draft(), Token)).Pending!;

        _clock.Advance(PendingProviderStore.Lifetime + TimeSpan.FromSeconds(1));

        _service.GetPending(pending.Id).ShouldBeNull();
        (await _service.AddFetchedAsync(pending.Id, "gpt-5", Token)).ShouldBeNull();
    }

    [Fact]
    public async Task OnlyAFewDraftsAreKept_TheOldestIsForgotten()
    {
        var first = (await _service.FetchModelsAsync(Draft("First"), Token)).Pending!;
        for (var index = 0; index < PendingProviderStore.MaxDrafts; index++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await _service.FetchModelsAsync(Draft($"Next {index}"), Token);
        }

        _service.GetPending(first.Id).ShouldBeNull();
    }

    [Fact]
    public async Task TheDraftSeenByThePage_NeverHoldsTheKey()
    {
        var pending = (await _service.FetchModelsAsync(Draft(), Token)).Pending!;

        pending.ToString().ShouldNotContain(Key);
        System.Text.Json.JsonSerializer.Serialize(pending).ShouldNotContain(Key);
    }
}
