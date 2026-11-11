using LivingPaper.Web.Localization;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

public class TranslationCatalogTests
{
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Texts(params (string Language, string Key, string Text)[] entries) =>
        entries.GroupBy(entry => entry.Language)
            .ToDictionary(group => group.Key, group => (IReadOnlyDictionary<string, string>)group.ToDictionary(entry => entry.Key, entry => entry.Text));

    [Fact]
    public void TryGet_FindsATextOfALanguage()
    {
        var catalog = new TranslationCatalog(Texts(("en", "a", "Hello"), ("fr", "a", "Bonjour")));

        catalog.TryGet("fr", "a", out var text).ShouldBeTrue();
        text.ShouldBe("Bonjour");
    }

    [Fact]
    public void TryGet_DoesNotFallBackBy_Itself_ThatIsTheLocalizersJob()
    {
        var catalog = new TranslationCatalog(Texts(("en", "a", "Hello"), ("fr", "b", "Salut")));

        catalog.TryGet("fr", "a", out var text).ShouldBeFalse();
        text.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("de", "a")]
    [InlineData("fr", "unknown")]
    [InlineData("", "a")]
    [InlineData("fr", "")]
    public void TryGet_IsFalse_ForAnUnknownLanguageOrKey(string language, string key)
    {
        new TranslationCatalog(Texts(("en", "a", "Hello"), ("fr", "a", "Bonjour"))).TryGet(language, key, out _).ShouldBeFalse();
    }

    [Fact]
    public void English_IsRequired_BecauseItIsTheFallback()
    {
        Should.Throw<ArgumentException>(() => new TranslationCatalog(Texts(("fr", "a", "Bonjour"))));
    }

    [Fact]
    public void TheKeysOfALanguage_AreListed()
    {
        var catalog = new TranslationCatalog(Texts(("en", "a", "x"), ("en", "b", "y"), ("fr", "a", "z")));

        catalog.KeysOf("en").Order().ShouldBe(["a", "b"]);
        catalog.KeysOf("fr").ShouldBe(["a"]);
        catalog.KeysOf("de").ShouldBeEmpty();
    }

    [Fact]
    public void TheEmbeddedFiles_AreLoadedForEverySupportedLanguage()
    {
        var catalog = TranslationCatalog.LoadEmbedded(typeof(Program).Assembly);

        foreach (var language in SupportedLanguages.All)
        {
            catalog.KeysOf(language.Code).ShouldNotBeEmpty($"the file of {language.Code} is empty or was not embedded");
        }
    }

    [Fact]
    public void AnAssemblyWithoutTheFiles_IsAnExplicitError()
    {
        var exception = Should.Throw<TranslationFileException>(() => TranslationCatalog.LoadEmbedded(typeof(object).Assembly));

        exception.FileName.ShouldBe("en.json");
    }
}
