using System.Text;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

public sealed class JsonLlmSettingsRepositoryTests : IDisposable
{
    private const string Key = "sk-this-is-the-secret-key";
    private const string IdA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string IdB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly TempDirectory _temp = new();
    private readonly ReversibleSecretProtector _protector = new();
    private readonly JsonLlmSettingsRepository _repository;

    public JsonLlmSettingsRepositoryTests()
    {
        Directory.CreateDirectory(_temp.Path);
        _repository = new JsonLlmSettingsRepository(_temp.Path, _protector);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string SettingsFile => Path.Combine(_temp.Path, JsonLlmSettingsRepository.FileName);

    private static LlmProvider Provider(string id, string name, string key = Key) =>
        new(id, name, "https://api.openai.com/v1", key, "gpt-5-nano");

    private static LlmSettings TwoProviders()
    {
        var settings = new LlmSettings();
        settings.Add(Provider(IdA, "OpenAI"));
        settings.Add(Provider(IdB, "Local", key: string.Empty));
        settings.SelectForChat(IdB);
        return settings;
    }

    private async Task WriteFileAsync(string content) => await File.WriteAllTextAsync(SettingsFile, content, Token);

    // A settings file as the application writes it, with the given provider entries.
    private static string FileWith(params string[] providerEntries) =>
        "{ \"schemaVersion\": 1, \"providers\": [" + string.Join(", ", providerEntries) + "] }";

    private static string Entry(string id = IdA, string name = "OpenAI", string baseUrl = "https://api.openai.com/v1", string model = "gpt", string? protectedKey = null) =>
        "{ \"id\": \"" + id + "\", \"name\": \"" + name + "\", \"baseUrl\": \"" + baseUrl + "\", \"model\": \"" + model + "\""
        + (protectedKey is null ? string.Empty : ", \"protectedApiKey\": \"" + protectedKey + "\"") + " }";

    // ---- Reading what was never saved ----

    [Fact]
    public async Task Load_ReturnsEmptySettings_WhenNothingWasEverSaved()
    {
        var settings = await _repository.LoadAsync(Token);

        settings.Providers.ShouldBeEmpty();
        settings.IsAnalysisEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Load_ReturnsEmptySettings_WhenTheDataDirectoryDoesNotExistYet()
    {
        var repository = new JsonLlmSettingsRepository(Path.Combine(_temp.Path, "not-yet"), _protector);

        (await repository.LoadAsync(Token)).Providers.ShouldBeEmpty();
    }

    // ---- Saving and loading ----

    [Fact]
    public async Task SaveThenLoad_GivesBackTheModelsTheChoicesAndTheKeys()
    {
        await _repository.SaveAsync(TwoProviders(), Token);

        var loaded = await _repository.LoadAsync(Token);

        loaded.Providers.ShouldBe([Provider(IdA, "OpenAI"), Provider(IdB, "Local", key: string.Empty)]);
        loaded.AnalysisProviderId.ShouldBe(IdA);
        loaded.ChatProviderId.ShouldBe(IdB);
        loaded.UnreadableKeyProviderIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_NeverWritesAKeyInClear()
    {
        await _repository.SaveAsync(TwoProviders(), Token);

        var text = await File.ReadAllTextAsync(SettingsFile, Token);

        text.ShouldNotContain(Key);
        text.ShouldNotContain("secret-key");
        text.ShouldContain("protectedApiKey");
        _protector.Protected.ShouldBe([Key]); // Only the key that exists was encrypted.
    }

    [Fact]
    public async Task Save_WritesOnlyTheDocumentedFields_WithASchemaVersion()
    {
        await _repository.SaveAsync(TwoProviders(), Token);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsFile, Token));
        json.RootElement.EnumerateObject().Select(property => property.Name).Order().ShouldBe(["analysisProviderId", "chatProviderId", "providers", "schemaVersion"]);
        json.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        var providers = json.RootElement.GetProperty("providers").EnumerateArray().ToList();
        providers[0].EnumerateObject().Select(property => property.Name).Order().ShouldBe(["baseUrl", "id", "model", "name", "protectedApiKey"]);
        providers[1].EnumerateObject().Select(property => property.Name).Order().ShouldBe(["baseUrl", "id", "model", "name"]); // No key, no field.
    }

    [Fact]
    public async Task Save_WritesAReadableIndentedFile_ThatKeepsAccents()
    {
        var settings = new LlmSettings();
        settings.Add(new LlmProvider(IdA, "Mon modèle local", "http://localhost:11434/v1", string.Empty, "llama3"));

        await _repository.SaveAsync(settings, Token);

        var text = await File.ReadAllTextAsync(SettingsFile, Token);
        text.ShouldContain("\n");
        text.ShouldContain("Mon modèle local");
    }

    [Fact]
    public async Task Save_OmitsTheChoices_WhenNothingIsChosen()
    {
        var settings = TwoProviders();
        settings.SelectForAnalysis(null);
        settings.SelectForChat(null);

        await _repository.SaveAsync(settings, Token);

        var loaded = await _repository.LoadAsync(Token);
        loaded.AnalysisProviderId.ShouldBeNull();
        loaded.ChatProviderId.ShouldBeNull();
        loaded.Providers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Save_CreatesTheDataDirectory_AndReplacesTheFile()
    {
        var repository = new JsonLlmSettingsRepository(Path.Combine(_temp.Path, "nested", "data"), _protector);
        await repository.SaveAsync(TwoProviders(), Token);

        var settings = TwoProviders();
        settings.Remove(IdB);
        await repository.SaveAsync(settings, Token);

        (await repository.LoadAsync(Token)).Providers.Select(provider => provider.Id).ShouldBe([IdA]);
    }

    [Fact]
    public async Task Save_LeavesNoTemporaryFileBehind()
    {
        await _repository.SaveAsync(TwoProviders(), Token);
        await _repository.SaveAsync(TwoProviders(), Token);

        Directory.GetFiles(_temp.Path).ShouldBe([SettingsFile]);
    }

    [Fact]
    public async Task Save_WhenTheFileCannotBeReplaced_Throws_AndRemovesItsTemporaryFile()
    {
        Directory.CreateDirectory(SettingsFile); // A directory where the file should go: the final rename must fail.

        await Should.ThrowAsync<Exception>(() => _repository.SaveAsync(TwoProviders(), Token));

        Directory.GetFileSystemEntries(_temp.Path).ShouldBe([SettingsFile]);
    }

    // ---- A file that cannot be used ----

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{ \"schemaVersion\": 99, \"providers\": [] }")]
    [InlineData("{ \"schemaVersion\": 1, \"providers\": {} }")]
    public async Task Load_ThrowsAClearError_ForAFileThatIsNotValid_InsteadOfStartingFromNothing(string content)
    {
        await WriteFileAsync(content);

        var exception = await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));

        exception.FileName.ShouldBe("settings.json");
    }

    [Fact]
    public async Task Load_ThrowsAClearError_ForAFileMuchBiggerThanAnySettings()
    {
        await WriteFileAsync(new string(' ', JsonLlmSettingsRepository.MaxFileBytes + 1));

        await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));
    }

    [Theory]
    [InlineData("not-a-valid-id")]
    [InlineData("../../etc/passwd")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("")]
    public async Task Load_RefusesAModelWithAnIdThatIsNotAnIdOfTheApplication(string id)
    {
        await WriteFileAsync(FileWith(Entry(id: id)));

        var exception = await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));

        exception.Problem.ShouldContain("id");
    }

    [Fact]
    public async Task Load_RefusesTwoModelsWithTheSameIdOrTheSameName()
    {
        await WriteFileAsync(FileWith(Entry(IdA, "One"), Entry(IdA, "Two")));
        await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));

        await WriteFileAsync(FileWith(Entry(IdA, "Same"), Entry(IdB, "same")));
        await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));
    }

    [Theory]
    [InlineData("ftp://example.com/v1", "baseUrl", "scheme")]
    [InlineData("https://user:pw@example.com/v1", "baseUrl", "credentials")]
    [InlineData("not a url", "baseUrl", "invalid")]
    public async Task Load_RefusesAModelWhoseFieldsBreakTheRules_NamingTheFieldButNotTheValue(string baseUrl, string field, string code)
    {
        await WriteFileAsync(FileWith(Entry(baseUrl: baseUrl)));

        var exception = await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));

        exception.Problem.ShouldContain(field);
        exception.Problem.ShouldContain(code);
        exception.Message.ShouldNotContain("pw@");
        exception.Message.ShouldNotContain("example.com");
    }

    [Fact]
    public async Task Load_RefusesAModelWithoutAName()
    {
        await WriteFileAsync("{ \"schemaVersion\": 1, \"providers\": [ { \"id\": \"" + IdA + "\", \"baseUrl\": \"https://example.com\", \"model\": \"m\" } ] }");

        var exception = await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));

        exception.Problem.ShouldContain("name");
    }

    [Fact]
    public async Task Load_RefusesMoreModelsThanTheMaximum()
    {
        var entries = Enumerable.Range(0, LlmProviderLimits.MaxProviders + 1).Select(index => Entry(index.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), $"Name {index}"));
        await WriteFileAsync(FileWith([.. entries]));

        await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));
    }

    [Fact]
    public async Task Load_NeverPutsAKeyOrAValueInTheError()
    {
        await WriteFileAsync(FileWith(Entry(name: "SECRET-NAME", baseUrl: "ftp://SECRET-HOST/v1", protectedKey: "SECRET-CIPHERTEXT")));

        var exception = await Should.ThrowAsync<SettingsFileException>(() => _repository.LoadAsync(Token));

        exception.Message.ShouldNotContain("SECRET");
    }

    [Fact]
    public async Task Load_IgnoresFieldsItDoesNotKnow_AndAcceptsNoModelAtAll()
    {
        await WriteFileAsync("{ \"schemaVersion\": 1, \"note\": \"mine\" }");

        (await _repository.LoadAsync(Token)).Providers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Load_DropsAChoiceOfAModelThatDoesNotExist()
    {
        await WriteFileAsync("{ \"schemaVersion\": 1, \"providers\": [" + Entry() + "], \"analysisProviderId\": \"" + IdB + "\", \"chatProviderId\": \"" + IdA + "\" }");

        var settings = await _repository.LoadAsync(Token);

        settings.AnalysisProviderId.ShouldBeNull();
        settings.ChatProviderId.ShouldBe(IdA);
    }

    [Fact]
    public async Task Load_TreatsAnEmptyProtectedKeyAsNoKey()
    {
        await WriteFileAsync(FileWith(Entry(protectedKey: string.Empty)));

        var settings = await _repository.LoadAsync(Token);

        settings.Providers.Single().ApiKey.ShouldBeEmpty();
        settings.UnreadableKeyProviderIds.ShouldBeEmpty();
    }

    // ---- A key that cannot be decrypted ----

    [Fact]
    public async Task Load_FlagsAKeyItCannotDecrypt_WithoutFailingAndWithoutMakingTheModelUsable()
    {
        await WriteFileAsync(FileWith(Entry(IdA, "Lost", protectedKey: "lost:abc"), Entry(IdB, "Fine", protectedKey: _protector.Protect(Key))));

        var settings = await _repository.LoadAsync(Token);

        settings.Providers.Count.ShouldBe(2);
        settings.Providers.Single(provider => provider.Id == IdA).ApiKey.ShouldBeEmpty();
        settings.Providers.Single(provider => provider.Id == IdB).ApiKey.ShouldBe(Key);
        settings.UnreadableKeyProviderIds.ShouldBe([IdA]);
    }

    [Fact]
    public async Task Save_KeepsTheStoredKeyOfAModelWhoseKeyCannotBeRead_WhenSomethingElseChanges()
    {
        await WriteFileAsync(FileWith(Entry(IdA, "Lost", protectedKey: "lost:abc"), Entry(IdB, "Fine", protectedKey: _protector.Protect(Key))));
        var settings = await _repository.LoadAsync(Token);

        settings.Remove(IdB); // An unrelated change: it must not wipe the key that could not be read.
        await _repository.SaveAsync(settings, Token);

        (await File.ReadAllTextAsync(SettingsFile, Token)).ShouldContain("lost:abc");
        var reloaded = await _repository.LoadAsync(Token);
        reloaded.UnreadableKeyProviderIds.ShouldBe([IdA]);
        reloaded.IsAnalysisEnabled.ShouldBeFalse();
    }

    // ---- Fake protector ----

    // A reversible stand-in for the encryption, so these tests are about the file and not about cryptography (see the
    // DataProtectionSecretProtector tests). Anything that starts with "lost:" cannot be decrypted.
    private sealed class ReversibleSecretProtector : ISecretProtector
    {
        public List<string> Protected { get; } = [];

        public string Protect(string secret)
        {
            Protected.Add(secret);
            return "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string([.. secret.Reverse()])));
        }

        public bool TryUnprotect(string protectedText, out string secret)
        {
            secret = string.Empty;
            if (!protectedText.StartsWith("enc:", StringComparison.Ordinal))
            {
                return false;
            }

            secret = new string([.. Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[4..])).Reverse()]);
            return true;
        }
    }
}
