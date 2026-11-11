using LivingPaper.Web.Localization;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

// A translation file is flat "key": "text" pairs. Anything else is an error that names the file and the key, never a value.
public class TranslationFileTests
{
    private const string File = "fr.json";

    [Fact]
    public void AValidFile_GivesItsPairs()
    {
        var texts = TranslationFile.Parse("""{ "home.tagline": "Discutez avec les personnages", "layout.skip": "Aller au contenu" }""", File);

        texts.Count.ShouldBe(2);
        texts["home.tagline"].ShouldBe("Discutez avec les personnages");
        texts["layout.skip"].ShouldBe("Aller au contenu");
    }

    [Fact]
    public void AccentsQuotationMarksEscapesAndVariables_AreKept()
    {
        var texts = TranslationFile.Parse("""{ "a": "« Où est ma canne, {name} ? »", "b": "ligne\nsuivante é", "c": "\"cité\"" }""", File);

        texts["a"].ShouldBe("« Où est ma canne, {name} ? »");
        texts["b"].ShouldBe("ligne\nsuivante é");
        texts["c"].ShouldBe("\"cité\"");
    }

    [Fact]
    public void AnEmptyObject_IsAnEmptyFile()
    {
        TranslationFile.Parse("{}", File).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("{ \"a\": \"x\", }")]
    [InlineData("{ // comment\n \"a\": \"x\" }")]
    public void AFileThatIsNotAnObjectOfPairs_IsRefused_NamingTheFile(string json)
    {
        var exception = Should.Throw<TranslationFileException>(() => TranslationFile.Parse(json, File));

        exception.FileName.ShouldBe(File);
        exception.Message.ShouldContain(File);
    }

    [Theory]
    [InlineData("""{ "a": 42 }""")]
    [InlineData("""{ "a": null }""")]
    [InlineData("""{ "a": true }""")]
    [InlineData("""{ "a": ["x"] }""")]
    [InlineData("""{ "a": { "b": "nested" } }""")]
    public void AValueThatIsNotText_IsRefused_NamingTheKey(string json)
    {
        var exception = Should.Throw<TranslationFileException>(() => TranslationFile.Parse(json, File));

        exception.Key.ShouldBe("a");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void AnEmptyText_IsRefused_NamingTheKey(string value)
    {
        var json = "{ \"home.title\": \"" + value.Replace("\n", "\\n", StringComparison.Ordinal) + "\" }";

        Should.Throw<TranslationFileException>(() => TranslationFile.Parse(json, File)).Key.ShouldBe("home.title");
    }

    [Theory]
    [InlineData("<b>bold</b>")]
    [InlineData("a < b")]
    [InlineData("x > y")]
    [InlineData("<script>alert(1)</script>")]
    public void HtmlInAText_IsRefused_BecauseTextsAreText(string value)
    {
        var json = "{ \"note\": \"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\" }";

        var exception = Should.Throw<TranslationFileException>(() => TranslationFile.Parse(json, File));

        exception.Key.ShouldBe("note");
        exception.Message.ShouldNotContain(value); // The message never repeats what the file says.
    }

    [Theory]
    [InlineData("")]
    [InlineData(" a")]
    [InlineData("a ")]
    [InlineData("a b")]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("é")]
    [InlineData("a/b")]
    [InlineData("a-b")]
    [InlineData("a{b}")]
    public void AKeyThatIsNotMadeOfLettersDigitsUnderscoresAndSingleDots_IsRefused(string key)
    {
        var json = "{ \"" + key + "\": \"text\" }";

        Should.Throw<TranslationFileException>(() => TranslationFile.Parse(json, File));
    }

    [Theory]
    [InlineData("home")]
    [InlineData("home.tagline")]
    [InlineData("layout.skipToContent")]
    [InlineData("settings.error.name_taken")]
    [InlineData("book.characters.one")]
    [InlineData("a1.b2")]
    public void ADottedKey_IsAccepted(string key)
    {
        TranslationFile.Parse("{ \"" + key + "\": \"text\" }", File).ShouldContainKey(key);
    }

    [Fact]
    public void ATwiceDefinedKey_IsRefused_BecauseTheSecondWouldSilentlyWin()
    {
        var exception = Should.Throw<TranslationFileException>(() => TranslationFile.Parse("""{ "a": "first", "a": "second" }""", File));

        exception.Key.ShouldBe("a");
    }

    [Fact]
    public void KeysAreCaseSensitive()
    {
        var texts = TranslationFile.Parse("""{ "Home": "one", "home": "two" }""", File);

        texts.Count.ShouldBe(2);
    }

    [Fact]
    public void TheError_NeverContainsAValue()
    {
        var exception = Should.Throw<TranslationFileException>(() => TranslationFile.Parse("""{ "ok": "SECRET-VALUE", "bad key": "SECRET-OTHER" }""", File));

        exception.Message.ShouldNotContain("SECRET");
    }
}
