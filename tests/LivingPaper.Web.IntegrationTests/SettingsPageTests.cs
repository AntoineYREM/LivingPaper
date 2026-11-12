using System.Net;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// The /Settings page (PAPER-5), on the real application with a language model that never leaves the process: the user adds
// and deletes language models and chooses which one analyzes books and which one chats. The key is typed in the page and
// must never come back in it.
public partial class SettingsPageTests
{
    private const string Key = "sk-page-secret-key-24680";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    // Plays the provider: answers "pong", or fails the way the test asks. Never a real call.
    private sealed class ScriptedChatModelFactory : IChatModelFactory
    {
        public Exception? Failure { get; set; }

        public IChatModel Create(LlmProvider provider, bool retryPassingFailures = true) => new ScriptedModel(this);

        private sealed class ScriptedModel(ScriptedChatModelFactory owner) : IChatModel
        {
            public IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> chatMessages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> chatMessages, ChatUsageMeter? usage, CancellationToken cancellationToken) =>
                owner.Failure is { } failure ? Task.FromException<string>(failure) : Task.FromResult("pong");
        }
    }

    // Plays the provider's list of models: the models it offers, or the failure the test asks for. Never a real call.
    private sealed class ScriptedModelCatalog : IModelCatalog
    {
        public IReadOnlyList<string> Models { get; set; } = ["gpt-5", "gpt-5-nano", "llama3.1"];

        public ConnectionFailure? Failure { get; set; }

        public int? Status { get; set; }

        public Task<ModelListResult> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(Failure is { } failure ? ModelListResult.Failed(failure, Status) : ModelListResult.Found(Models));
    }

    private static LivingPaperFactory NewFactory(ScriptedChatModelFactory models, ScriptedModelCatalog? catalog = null) =>
        LivingPaperFactory.With(configureServices: services =>
        {
            services.AddSingleton<IChatModelFactory>(models);
            services.AddSingleton<IModelCatalog>(catalog ?? new ScriptedModelCatalog());
        });

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> GetAsync(HttpClient client, string path = "/Settings", string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await client.SendAsync(request, Token);
        return await response.Content.ReadAsStringAsync(Token);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string handler, Dictionary<string, string> form, bool withToken = true, string? acceptLanguage = null)
    {
        if (withToken)
        {
            form["__RequestVerificationToken"] = AntiforgeryToken().Match(await GetAsync(client)).Groups[1].Value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Settings?handler=" + handler) { Content = new FormUrlEncodedContent(form) };
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return await client.SendAsync(request, Token);
    }

    // The first step of adding a model: the name, the address and the key.
    private static Dictionary<string, string> NewProvider(string name = "OpenAI", string baseUrl = "https://api.openai.com/v1", string key = Key) =>
        new() { ["name"] = name, ["baseUrl"] = baseUrl, ["apiKey"] = key };

    // The identifier of the draft, in the address the first step goes back to: /Settings?draft=...#add
    private static string DraftOf(HttpResponseMessage response) =>
        Regex.Match(response.Headers.Location!.OriginalString, "draft=([0-9a-f]+)").Groups[1].Value;

    // Both steps: get the models, then save the chosen one. Returns the page that follows.
    private static async Task<string> AddAsync(HttpClient client, string name = "OpenAI", string model = "gpt-5-nano", string baseUrl = "https://api.openai.com/v1", string key = Key)
    {
        using var fetched = await PostAsync(client, "Models", NewProvider(name, baseUrl, key));
        fetched.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var saved = await PostAsync(client, "Add", new Dictionary<string, string> { ["draft"] = DraftOf(fetched), ["model"] = model });
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return await GetAsync(client);
    }

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    // ---- The page ----

    [Fact]
    public async Task ThePage_IsBuiltWithTheSharedLayout_AndHasASingleMainTitle()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        using var response = await client.GetAsync("/Settings", Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<main id=\"main\"");
        Regex.Count(html, @"<h1\b").ShouldBe(1);
        html.ShouldContain("<title>Settings");
    }

    [Fact]
    public async Task WithNoModel_ThePageSaysAnalysisAndChatAreOff_AndOffersTheForm()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        var html = Decoded(await GetAsync(client));

        html.ShouldContain("No language model is saved yet");
        html.ShouldContain("Analysis is off");
        html.ShouldContain("Chat is off");
        html.ShouldContain("name=\"name\"");
        html.ShouldContain("name=\"baseUrl\"");
        html.ShouldContain("name=\"apiKey\"");
        html.ShouldContain("Get the models");
        html.ShouldNotContain("name=\"model\""); // The model is chosen in the second step, from the list of the provider.
        html.ShouldContain("type=\"password\"");
    }

    [Fact]
    public async Task EveryFormFieldHasAVisibleLabel_AndTheKeyFieldIsNotAutofilled()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        var html = await GetAsync(client);

        foreach (var field in new[] { "llm-name", "llm-base-url", "llm-api-key" })
        {
            html.ShouldContain($"for=\"{field}\"");
            html.ShouldContain($"id=\"{field}\"");
        }

        Regex.IsMatch(html, @"<input[^>]*type=""password""[^>]*autocomplete=""(off|new-password)""").ShouldBeTrue();
    }

    [Fact]
    public async Task TheWaitForTheListOfModels_IsAnnounced()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        (await GetAsync(client)).ShouldContain("takes a few seconds");
    }

    [Fact]
    public async Task TheSecondStep_OffersTheModelsOfTheProviderInAListbox_WithALabel_AndNeverTheKey()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        using var fetched = await PostAsync(client, "Models", NewProvider());

        fetched.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        fetched.Headers.Location!.OriginalString.ShouldEndWith("#add");
        var html = Decoded(await GetAsync(client, fetched.Headers.Location.OriginalString.Split('#')[0]));

        html.ShouldContain("The key works");
        html.ShouldContain("for=\"llm-model\"");
        Regex.IsMatch(html, @"<select[^>]*id=""llm-model""[^>]*name=""model""[^>]*size=""3""[^>]*required").ShouldBeTrue();
        Regex.Matches(html, @"<option value=""([^""]+)"">").Select(match => match.Groups[1].Value).ShouldBe(["gpt-5", "gpt-5-nano", "llama3.1"]);
        html.ShouldContain("value=\"" + DraftOf(fetched) + "\"");
        html.ShouldContain("https://api.openai.com/v1");
        html.ShouldNotContain(Key);
        Regex.IsMatch(html, @"<input[^>]*name=""apiKey""").ShouldBeFalse("the key is not asked twice");
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse("nothing is saved before the user chooses");
    }

    [Fact]
    public async Task TheSecondStep_AModelOutsideTheList_IsRefused_AndTheUserCanChooseAgain()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        using var fetched = await PostAsync(client, "Models", NewProvider());

        using var response = await PostAsync(client, "Add", new Dictionary<string, string> { ["draft"] = DraftOf(fetched), ["model"] = "invented" });
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Error: this model is not in the list.");
        html.ShouldContain("<option value=\"gpt-5\">");
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task AnExpiredOrUnknownDraft_SaysSo_AndOffersTheFirstStepAgain()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        var onGet = Decoded(await GetAsync(client, "/Settings?draft=0123456789abcdef0123456789abcdef"));
        using var onPost = await PostAsync(client, "Add", new Dictionary<string, string> { ["draft"] = "0123456789abcdef0123456789abcdef", ["model"] = "gpt-5" });
        var afterPost = Decoded(await onPost.Content.ReadAsStringAsync(Token));

        onGet.ShouldContain("This draft is no longer available");
        onGet.ShouldContain("Get the models");
        afterPost.ShouldContain("This draft is no longer available");
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheProviderHasNoList_TheUserTypesTheModel_ItIsTestedWithARealCall_ThenSaved()
    {
        var chat = new ScriptedChatModelFactory();
        using var factory = NewFactory(chat, new ScriptedModelCatalog { Failure = ConnectionFailure.NoModelList, Status = 404 });
        using var client = NewClient(factory);
        using var fetched = await PostAsync(client, "Models", NewProvider(name: "Local", baseUrl: "http://localhost:8080/v1", key: string.Empty));
        var page = Decoded(await GetAsync(client, fetched.Headers.Location!.OriginalString.Split('#')[0]));

        page.ShouldContain("does not offer a list of its models");
        Regex.IsMatch(page, @"<input[^>]*id=""llm-model""[^>]*type=""text""").ShouldBeTrue();
        page.ShouldContain("up to a minute");

        using var saved = await PostAsync(client, "Add", new Dictionary<string, string> { ["draft"] = DraftOf(fetched), ["model"] = "my-model" });
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Decoded(await GetAsync(client)).ShouldContain("my-model");
    }

    [Theory]
    [InlineData(ChatFailureKind.Http, 404, "The provider does not know this model (HTTP 404)")]
    [InlineData(ChatFailureKind.Http, 401, "The key was refused (HTTP 401)")]
    [InlineData(ChatFailureKind.Unreachable, null, "Nothing answered at this address")]
    [InlineData(ChatFailureKind.InvalidResponse, null, "The provider answered, but not with a usable reply")]
    public async Task WhenTheProviderHasNoList_ARefusedConnectionTest_SavesNothing_AndKeepsTheDraft(ChatFailureKind kind, int? status, string expected)
    {
        var chat = new ScriptedChatModelFactory { Failure = new ChatProviderException(kind, "failed", status) };
        using var factory = NewFactory(chat, new ScriptedModelCatalog { Failure = ConnectionFailure.NoModelList });
        using var client = NewClient(factory);
        using var fetched = await PostAsync(client, "Models", NewProvider());

        using var response = await PostAsync(client, "Add", new Dictionary<string, string> { ["draft"] = DraftOf(fetched), ["model"] = "typed-model" });
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain(expected);
        html.ShouldContain("role=\"alert\"");
        html.ShouldNotContain(Key);
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task EveryFormOfThePage_CarriesTheAntiforgeryToken()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        var html = await AddAsync(client);

        var forms = Regex.Matches(html, @"<form\b[^>]*method=""post""[\s\S]*?</form>", RegexOptions.IgnoreCase).Select(match => match.Value).Where(form => !form.Contains("/Language", StringComparison.Ordinal)).ToList();

        forms.Count.ShouldBeGreaterThanOrEqualTo(3); // Get the models, delete, choose.
        forms.ShouldAllBe(form => form.Contains("__RequestVerificationToken", StringComparison.Ordinal));
    }

    // ---- Adding a model ----

    [Fact]
    public async Task AddingAModel_ChoosesItInTheList_SavesItEncrypted_WithoutAnotherTest_AndGoesBackToThePageWithANotice()
    {
        var models = new ScriptedChatModelFactory { Failure = new InvalidOperationException("the chat model must not be called: the list proved the key") };
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        using var fetched = await PostAsync(client, "Models", NewProvider());

        using var response = await PostAsync(client, "Add", new Dictionary<string, string> { ["draft"] = DraftOf(fetched), ["model"] = "gpt-5-nano" });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldStartWith("/Settings");
        var html = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString));
        html.ShouldContain("OpenAI was added");
        html.ShouldContain("https://api.openai.com/v1");
        html.ShouldContain("gpt-5-nano");
        html.ShouldContain("Saved, encrypted");
        var settingsFile = await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "settings.json"), Token);
        settingsFile.ShouldNotContain(Key);
    }

    [Fact]
    public async Task TheFirstModel_IsChosenForAnalysisAndChat_AndThePageSaysSo()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        var html = Decoded(await AddAsync(client));

        html.ShouldContain("For analysis");
        html.ShouldContain("For chat");
        html.ShouldNotContain("Analysis is off");
        html.ShouldNotContain("Chat is off");
    }

    [Fact]
    public async Task TheKey_NeverComesBackInThePage_NotAfterAnAdd_NotInTheList_NotInAnError()
    {
        var models = new ScriptedChatModelFactory();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);

        var catalog = new ScriptedModelCatalog();
        using var withCatalog = NewFactory(models, catalog);
        using var catalogClient = NewClient(withCatalog);

        var afterAdd = await AddAsync(catalogClient);
        catalog.Failure = ConnectionFailure.KeyRefused;
        catalog.Status = 401;
        using var failed = await PostAsync(catalogClient, "Models", NewProvider(name: "Other"));
        var afterFailure = await failed.Content.ReadAsStringAsync(Token);
        using var invalid = await PostAsync(catalogClient, "Models", NewProvider(name: string.Empty));
        var afterInvalid = await invalid.Content.ReadAsStringAsync(Token);
        catalog.Failure = null;
        using var fetched = await PostAsync(catalogClient, "Models", NewProvider(name: "Third"));
        var secondStep = await GetAsync(catalogClient, fetched.Headers.Location!.OriginalString.Split('#')[0]);

        afterAdd.ShouldNotContain(Key);
        afterFailure.ShouldNotContain(Key);
        afterInvalid.ShouldNotContain(Key);
        secondStep.ShouldNotContain(Key);
        (await GetAsync(catalogClient)).ShouldNotContain(Key);
        (await GetAsync(client)).ShouldNotContain(Key);
    }

    [Fact]
    public async Task AnInvalidForm_IsShownAgainWithWhatWasTyped_ExceptTheKey_AndErrorsBesideTheirFields()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        using var response = await PostAsync(client, "Models", NewProvider(name: string.Empty, baseUrl: "ftp://files.example.com"));
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("value=\"ftp://files.example.com\"");
        html.ShouldContain("Error: this field is required.");
        html.ShouldContain("Error: the address must start with http:// or https://.");
        Regex.Count(html, "aria-invalid=\"true\"").ShouldBe(2);
        Regex.IsMatch(html, @"<input[^>]*id=""llm-name""[^>]*aria-describedby=""[^""]*llm-name-error").ShouldBeTrue();
        Regex.IsMatch(html, @"<input[^>]*type=""password""[^>]*value=").ShouldBeFalse("the key is never sent back");
        html.ShouldContain("type the key again");
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task ANameAlreadyUsed_IsRefusedBesideTheNameField()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        await AddAsync(client);

        using var response = await PostAsync(client, "Models", NewProvider(name: " openai "));
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        html.ShouldContain("Error: another language model already has this name.");
    }

    [Theory]
    [InlineData(ConnectionFailure.Unreachable, null, "Nothing answered at this address")]
    [InlineData(ConnectionFailure.KeyRefused, 401, "The key was refused (HTTP 401)")]
    [InlineData(ConnectionFailure.KeyRefused, 403, "The key was refused (HTTP 403)")]
    [InlineData(ConnectionFailure.ProviderError, 429, "The provider reported an error (HTTP 429)")]
    [InlineData(ConnectionFailure.ProviderError, 500, "The provider reported an error (HTTP 500)")]
    public async Task AProviderThatRefusesToGiveItsModels_SavesNothing_AndSaysWhatToFix(ConnectionFailure failure, int? status, string expected)
    {
        using var factory = NewFactory(new ScriptedChatModelFactory(), new ScriptedModelCatalog { Failure = failure, Status = status });
        using var client = NewClient(factory);

        using var response = await PostAsync(client, "Models", NewProvider(name: "Typed name"));
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain(expected);
        html.ShouldContain("role=\"alert\"");
        html.ShouldContain("value=\"Typed name\"");
        html.ShouldNotContain(Key);
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse();
        (await GetAsync(client)).ShouldNotContain("Typed name");
    }

    [Fact]
    public async Task AnUnusablePromptsFile_IsReportedByTheConnectionTest_OfAProviderWithNoList_WithoutBeingOverwritten()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory(), new ScriptedModelCatalog { Failure = ConnectionFailure.NoModelList });
        using var client = NewClient(factory);
        var path = Path.Combine(factory.DataDirectory, "prompts.json");
        await GetAsync(client); // Starts the application, which creates prompts.json.
        await File.WriteAllTextAsync(path, "{ not json", Token);

        using var fetched = await PostAsync(client, "Models", NewProvider());
        using var response = await PostAsync(client, "Add", new Dictionary<string, string> { ["draft"] = DraftOf(fetched), ["model"] = "typed-model" });
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        html.ShouldContain("prompts.json");
        html.ShouldContain("cannot be used");
        (await File.ReadAllTextAsync(path, Token)).ShouldBe("{ not json");
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused_AndSavesNothing()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        using var response = await PostAsync(client, "Models", NewProvider(), withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        File.Exists(Path.Combine(factory.DataDirectory, "settings.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task TheListOfModels_AndTheKey_NeverReachTheLogs()
    {
        var catalog = new ScriptedModelCatalog();
        using var factory = NewFactory(new ScriptedChatModelFactory(), catalog);
        using var client = NewClient(factory);

        await AddAsync(client);
        catalog.Failure = ConnectionFailure.KeyRefused;
        catalog.Status = 401;
        (await PostAsync(client, "Models", NewProvider(name: "Other"))).Dispose();

        factory.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(Key, StringComparison.Ordinal));
    }

    // ---- The list ----

    [Fact]
    public async Task AModelWithNoKey_SaysNoKeyIsNeeded_AndAnUnreadableKeyAsksForTheModelToBeAddedAgain()
    {
        var models = new ScriptedChatModelFactory();
        using var factory = NewFactory(models);
        using var client = NewClient(factory);
        var html = Decoded(await AddAsync(client, name: "Local", baseUrl: "http://localhost:11434/v1", key: string.Empty, model: "llama3.1"));

        html.ShouldContain("None saved");
        html.ShouldContain("http://localhost:11434/v1");
    }

    [Fact]
    public async Task AKeyThatCannotBeReadAnyMore_IsSaid_AndThatTheModelMustBeAddedAgain()
    {
        var models = new ScriptedChatModelFactory();
        string dataDirectory;
        using (var first = NewFactory(models))
        {
            dataDirectory = first.DataDirectory;
            using var client = NewClient(first);
            await AddAsync(client);

            // The encryption keys are lost: the saved key cannot be decrypted. The first factory still owns the cleaning.
            foreach (var file in Directory.GetFiles(Path.Combine(dataDirectory, "keys")))
            {
                File.Delete(file);
            }

            using var second = LivingPaperFactory.With(dataDirectory, services => services.AddSingleton<IChatModelFactory>(models));
            using var secondClient = NewClient(second);
            var html = Decoded(await GetAsync(secondClient));

            html.ShouldContain("The key can no longer be read");
            html.ShouldContain("add the model again");
            html.ShouldNotContain(Key);
        }
    }

    // ---- Deleting a model ----

    [Fact]
    public async Task Deleting_AsksForConfirmation_WithoutScript_AndNoBrowserDialog()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        var html = Decoded(await AddAsync(client));

        html.ShouldContain("<details");
        html.ShouldContain("This deletes the model and its saved key");
        html.ShouldContain("Confirm the deletion");
        html.ShouldNotContain("onclick");
        html.ShouldNotContain("confirm(");
    }

    [Fact]
    public async Task ConfirmingTheDeletion_RemovesTheModel_AndTurnsAnalysisAndChatOffWhenItWasChosen()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        await AddAsync(client);
        var id = (await factory.Services.GetRequiredService<LlmSettingsService>().GetAsync(Token)).Providers.Single().Id;

        using var response = await PostAsync(client, "Delete", new Dictionary<string, string> { ["id"] = id });
        var html = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        html.ShouldContain("OpenAI was deleted");
        html.ShouldContain("Analysis is off");
        html.ShouldContain("Chat is off");
        (await File.ReadAllTextAsync(Path.Combine(factory.DataDirectory, "settings.json"), Token)).ShouldNotContain("gpt-5-nano");
    }

    [Fact]
    public async Task DeletingAModelThatDoesNotExist_IsANotFound()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        using var response = await PostAsync(client, "Delete", new Dictionary<string, string> { ["id"] = "nope" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Choosing which model does what ----

    [Fact]
    public async Task TheChoiceForm_OffersEveryModelAndNone_AndMarksTheChosenOnes()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        await AddAsync(client, "OpenAI");

        var html = Decoded(await AddAsync(client, "Local", "llama3.1"));

        html.ShouldContain("for=\"llm-analysis\"");
        html.ShouldContain("for=\"llm-chat\"");
        Regex.Count(html, @"<option value="""">None</option>").ShouldBe(2);
        Regex.Count(html, @"<option[^>]*selected[^>]*>OpenAI</option>").ShouldBe(2);
    }

    [Fact]
    public async Task ChoosingModels_SavesThem_AndAnEmptyChoiceTurnsThatUseOff()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        await AddAsync(client, "OpenAI");
        await AddAsync(client, "Local", "llama3.1");
        var service = factory.Services.GetRequiredService<LlmSettingsService>();
        var local = (await service.GetAsync(Token)).Providers.Single(provider => provider.Name == "Local").Id;

        using var response = await PostAsync(client, "Select", new Dictionary<string, string> { ["analysis"] = string.Empty, ["chat"] = local });
        var html = Decoded(await GetAsync(client, response.Headers.Location!.OriginalString));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var settings = await service.GetAsync(Token);
        settings.AnalysisProviderId.ShouldBeNull();
        settings.ChatProviderId.ShouldBe(local);
        html.ShouldContain("Analysis is off");
        html.ShouldNotContain("Chat is off");
    }

    [Fact]
    public async Task ChoosingAModelThatDoesNotExist_IsRefused_AndChangesNothing()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);
        await AddAsync(client);
        var service = factory.Services.GetRequiredService<LlmSettingsService>();
        var before = await service.GetAsync(Token);

        using var response = await PostAsync(client, "Select", new Dictionary<string, string> { ["analysis"] = "nope", ["chat"] = before.ChatProviderId! });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await service.GetAsync(Token)).AnalysisProviderId.ShouldBe(before.AnalysisProviderId);
    }

    // ---- When the settings file cannot be used ----

    [Fact]
    public async Task AnUnusableSettingsFile_ShowsAClearError_WithoutItsContent_AndLeavesTheFileAlone()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        var path = Path.Combine(factory.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(path, "{ SECRET-LOOKING TEXT, not json", Token);
        using var client = NewClient(factory);

        using var response = await client.GetAsync("/Settings", Token);
        var html = Decoded(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        html.ShouldContain("settings.json");
        html.ShouldContain("cannot be used");
        html.ShouldNotContain("SECRET-LOOKING");
        (await File.ReadAllTextAsync(path, Token)).ShouldBe("{ SECRET-LOOKING TEXT, not json");
    }

    // ---- Language and navigation ----

    [Fact]
    public async Task ThePage_IsTranslated_NoEnglishLeftBehind()
    {
        var catalog = new ScriptedModelCatalog { Failure = ConnectionFailure.KeyRefused, Status = 401 };
        using var factory = NewFactory(new ScriptedChatModelFactory(), catalog);
        using var client = NewClient(factory);

        var empty = Decoded(await GetAsync(client, acceptLanguage: "fr"));
        using var refused = await PostAsync(client, "Models", NewProvider(), acceptLanguage: "fr");
        var failure = Decoded(await refused.Content.ReadAsStringAsync(Token));

        empty.ShouldContain("<html lang=\"fr\"");
        empty.ShouldContain("Aucun modèle de langage");
        empty.ShouldContain("L’analyse est désactivée");
        failure.ShouldContain("La clé a été refusée (HTTP 401)");
        empty.ShouldNotContain("Analysis is off");
        failure.ShouldNotContain("The key was refused");
    }

    [Fact]
    public async Task TheRunningHead_LinksToTheSettings_InBothLanguages()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        var english = await GetAsync(client, "/");
        var french = Decoded(await GetAsync(client, "/", "fr"));

        Regex.IsMatch(english, @"<a[^>]*href=""/Settings""[^>]*>Settings</a>").ShouldBeTrue();
        Regex.IsMatch(french, @"<a[^>]*href=""/Settings""[^>]*>Paramètres</a>").ShouldBeTrue();
    }

    [Fact]
    public async Task TheSettingsLink_MarksThePageYouAreOn()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        Regex.IsMatch(await GetAsync(client), @"<a[^>]*href=""/Settings""[^>]*aria-current=""page""").ShouldBeTrue();
        Regex.IsMatch(await GetAsync(client, "/"), @"<a[^>]*href=""/Settings""[^>]*aria-current=""page""").ShouldBeFalse();
    }

    [Fact]
    public async Task TheLanguageSwitcher_OnThePage_ComesBackToTheSettings()
    {
        using var factory = NewFactory(new ScriptedChatModelFactory());
        using var client = NewClient(factory);

        (await GetAsync(client)).ShouldContain("value=\"/Settings\"");
    }

    // ---- The texts ----

    // The interface turns every code of the domain and every kind of failure into a sentence: a code added later without
    // its text fails here, in every language.
    public static TheoryData<string> LanguageCodes() => [.. SupportedLanguages().Select(language => language)];

    private static IEnumerable<string> SupportedLanguages() => ["en", "fr"];

    private static IReadOnlyDictionary<string, string> Texts(string language) =>
        LivingPaper.Web.Localization.TranslationFile.Parse(
            File.ReadAllText(Path.Combine(RepositoryFiles.Find(Path.Combine("src", "LivingPaper.Web")), "Resources", language + ".json")),
            language + ".json");

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void EveryErrorCodeOfAField_HasItsText(string language)
    {
        var codes = typeof(LlmProviderErrors).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!).ToList();
        var texts = Texts(language);

        codes.ShouldNotBeEmpty();
        foreach (var code in codes.Where(code => code != LlmProviderErrors.TooMany))
        {
            texts.Keys.ShouldContain("settings.error." + code, $"{language}.json lacks the text of the error code '{code}'");
        }

        texts.Keys.ShouldContain("settings.tooMany");
    }

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void EveryKindOfConnectionFailure_HasItsText(string language)
    {
        var texts = Texts(language);

        foreach (var failure in Enum.GetNames<ConnectionFailure>())
        {
            texts.Keys.ShouldContain("settings.failure." + char.ToLowerInvariant(failure[0]) + failure[1..], $"{language}.json lacks the text of the failure '{failure}'");
        }
    }
}
