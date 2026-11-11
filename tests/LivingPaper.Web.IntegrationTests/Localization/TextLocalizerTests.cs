using System.Globalization;
using LivingPaper.Web.Localization;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

public class TextLocalizerTests
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["a"] = "Hello",
        ["b"] = "Hi {name}",
        ["onlyEnglish"] = "Only in English",
        ["two"] = "{first} and {second}",
        ["books.one"] = "{count} book",
        ["books.other"] = "{count} books",
        ["onlyOne.one"] = "{count} thing",
        ["onlyOne.other"] = "{count} things",
    };

    private static readonly Dictionary<string, string> French = new()
    {
        ["a"] = "Bonjour",
        ["b"] = "Salut {name}",
        ["two"] = "{first} et {second}",
        ["books.one"] = "{count} livre",
        ["books.other"] = "{count} livres",
        ["onlyOne.other"] = "{count} choses",
    };

    private sealed class RecordingLogger : ILogger<TextLocalizer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static TranslationCatalog Catalog() => new(new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["en"] = English,
        ["fr"] = French,
    });

    private static (TextLocalizer Texts, LanguageContext Context, RecordingLogger Logger) NewLocalizer(string language = "en")
    {
        var context = new LanguageContext { Code = language };
        var logger = new RecordingLogger();
        return (new TextLocalizer(Catalog(), context, logger), context, logger);
    }

    // ---- Reading a text ----

    [Theory]
    [InlineData("en", "Hello")]
    [InlineData("fr", "Bonjour")]
    public void TheIndexer_GivesTheTextInTheCurrentLanguage(string language, string expected)
    {
        var (texts, _, logger) = NewLocalizer(language);

        texts["a"].ShouldBe(expected);
        texts.Language.ShouldBe(language);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void TheLanguageIsReadAtEveryCall_SoOneInstanceFollowsTheRequest()
    {
        var (texts, context, _) = NewLocalizer("en");

        texts["a"].ShouldBe("Hello");
        context.Code = "fr";
        texts["a"].ShouldBe("Bonjour");
    }

    [Fact]
    public void ALanguageThatIsNotSupported_IsTreatedAsEnglish()
    {
        var (texts, _, _) = NewLocalizer("de");

        texts["a"].ShouldBe("Hello");
        texts.Language.ShouldBe("en");
    }

    // ---- A text that is missing ----

    [Fact]
    public void AMissingText_FallsBackToEnglish_AndIsLoggedOnce()
    {
        var (texts, _, logger) = NewLocalizer("fr");

        texts["onlyEnglish"].ShouldBe("Only in English");
        texts["onlyEnglish"].ShouldBe("Only in English");

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("onlyEnglish");
        warning.Message.ShouldContain("fr");
        warning.Message.ShouldNotContain("Only in English"); // The key and the language, never the text.
    }

    [Fact]
    public void AKeyThatExistsNowhere_ShowsTheKey_AndIsLoggedAsAnError()
    {
        var (texts, _, logger) = NewLocalizer("fr");

        texts["no.such.key"].ShouldBe("no.such.key");
        texts["no.such.key"].ShouldBe("no.such.key");

        var error = logger.Entries.ShouldHaveSingleItem();
        error.Level.ShouldBe(LogLevel.Error);
        error.Message.ShouldContain("no.such.key");
    }

    [Fact]
    public void ADifferentMissingKey_IsLoggedToo()
    {
        var (texts, _, logger) = NewLocalizer("fr");

        _ = texts["no.such.key"];
        _ = texts["another.missing"];

        logger.Entries.Count.ShouldBe(2);
    }

    // ---- Variables ----

    [Theory]
    [InlineData("en", "Hi Argan")]
    [InlineData("fr", "Salut Argan")]
    public void Format_ReplacesTheVariablesByTheirName(string language, string expected)
    {
        var (texts, _, _) = NewLocalizer(language);

        texts.Format("b", new { name = "Argan" }).ShouldBe(expected);
    }

    [Fact]
    public void Format_ReplacesSeveralVariables_AndIgnoresTheOnesTheTextDoesNotUse()
    {
        var (texts, _, _) = NewLocalizer("fr");

        texts.Format("two", new { first = "Argan", second = "Toinette", unused = "x" }).ShouldBe("Argan et Toinette");
    }

    [Fact]
    public void Format_NeverReplacesAgainInsideAValue()
    {
        var (texts, _, _) = NewLocalizer("en");

        texts.Format("two", new { first = "{second}", second = "B" }).ShouldBe("{second} and B");
    }

    [Fact]
    public void Format_TakesNumbersAndNothing()
    {
        var (texts, _, _) = NewLocalizer("en");

        texts.Format("two", new { first = 8, second = (string?)null }).ShouldBe("8 and ");
    }

    [Fact]
    public void Format_WithAVariableTheCallerDidNotGive_KeepsThePlaceholder_AndLogsAnError()
    {
        var (texts, _, logger) = NewLocalizer("en");

        texts.Format("b", new { other = "x" }).ShouldBe("Hi {name}");

        var error = logger.Entries.ShouldHaveSingleItem();
        error.Level.ShouldBe(LogLevel.Error);
        error.Message.ShouldContain("b");
        error.Message.ShouldContain("name");
    }

    [Fact]
    public void Format_OfAKeyThatIsMissingInTheLanguage_UsesTheEnglishText()
    {
        var (texts, _, _) = NewLocalizer("fr");

        texts.Format("onlyEnglish", new { }).ShouldBe("Only in English");
    }

    // ---- Plurals ----

    [Theory]
    [InlineData("en", 0, "0 books")]
    [InlineData("en", 1, "1 book")]
    [InlineData("en", 2, "2 books")]
    [InlineData("en", 8, "8 books")]
    [InlineData("fr", 0, "0 livre")]
    [InlineData("fr", 1, "1 livre")]
    [InlineData("fr", 2, "2 livres")]
    [InlineData("fr", 8, "8 livres")]
    public void Plural_ChoosesTheFormOfTheLanguage(string language, long count, string expected)
    {
        var (texts, _, _) = NewLocalizer(language);

        texts.Plural("books", count).ShouldBe(expected);
    }

    [Fact]
    public void Plural_WritesBigNumbersTheWayTheLanguageDoes()
    {
        var (english, _, _) = NewLocalizer("en");
        var (french, _, _) = NewLocalizer("fr");

        english.Plural("books", 12345).ShouldBe($"{12345.ToString("N0", CultureInfo.GetCultureInfo("en"))} books");
        french.Plural("books", 12345).ShouldBe($"{12345.ToString("N0", CultureInfo.GetCultureInfo("fr"))} livres");
    }

    [Fact]
    public void Plural_ANegativeCount_IsAPlural()
    {
        var (texts, _, _) = NewLocalizer("en");

        texts.Plural("books", -1).ShouldBe($"{(-1).ToString("N0", CultureInfo.GetCultureInfo("en"))} books");
    }

    [Fact]
    public void Plural_AFormMissingInTheLanguage_FallsBackToTheEnglishOne_AndIsLogged()
    {
        var (texts, _, logger) = NewLocalizer("fr");

        texts.Plural("onlyOne", 1).ShouldBe("1 thing");

        logger.Entries.ShouldHaveSingleItem().Message.ShouldContain("onlyOne.one");
    }
}
