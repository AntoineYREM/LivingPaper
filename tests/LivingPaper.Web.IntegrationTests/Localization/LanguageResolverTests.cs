using LivingPaper.Web.Localization;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

// Which language a visitor gets: the cookie they chose, else the language of their browser, else English.
public class LanguageResolverTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void NothingKnown_GivesEnglish(string? cookie, string? acceptLanguage)
    {
        LanguageResolver.Resolve(cookie, acceptLanguage).ShouldBe("en");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("FR")]
    [InlineData("  fr  ")]
    [InlineData("Fr")]
    public void TheChosenLanguage_WinsOverTheBrowser(string cookie)
    {
        LanguageResolver.Resolve(cookie, "en-US,en;q=0.9").ShouldBe("fr");
    }

    [Theory]
    [InlineData("de")]
    [InlineData("zz")]
    [InlineData("fr-FR")]
    [InlineData("french")]
    [InlineData("../en")]
    [InlineData("fr; Path=/")]
    public void ACookieThatIsNotASupportedLanguage_IsIgnored(string cookie)
    {
        LanguageResolver.Resolve(cookie, "fr").ShouldBe("fr"); // The browser decides instead.
        LanguageResolver.Resolve(cookie, null).ShouldBe("en");
    }

    [Theory]
    [InlineData("fr-FR,fr;q=0.9,en;q=0.8", "fr")]
    [InlineData("en-US,en;q=0.9,fr;q=0.8", "en")]
    [InlineData("fr", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("FR-ca", "fr")]
    [InlineData("  fr-BE ; q=0.8 ", "fr")]
    [InlineData("de,fr;q=0.5", "fr")]
    [InlineData("de, es;q=0.8, fr;q=0.2", "fr")]
    [InlineData("fr;q=0.2, en;q=0.9", "en")]
    [InlineData("en;q=0.5, fr;q=0.9", "fr")]
    public void TheBrowserLanguage_IsChosenByItsPreferenceOrder(string header, string expected)
    {
        LanguageResolver.Resolve(null, header).ShouldBe(expected);
    }

    [Theory]
    [InlineData("de, es")]
    [InlineData("*")]
    [InlineData("de, *;q=0.5")]
    [InlineData("fr;q=0")]
    [InlineData("fr;q=0, en;q=0")]
    [InlineData("frx")]
    [InlineData("x-fr")]
    [InlineData("fr;q=abc")]
    [InlineData(";;;,,,")]
    [InlineData("fr;q=1.5")]
    public void ALanguageWeNeverOfferOrOneTheBrowserRefuses_GivesEnglish(string header)
    {
        LanguageResolver.Resolve(null, header).ShouldBe("en");
    }

    [Fact]
    public void ASameQuality_KeepsTheOrderOfTheHeader()
    {
        LanguageResolver.Resolve(null, "fr, en").ShouldBe("fr");
        LanguageResolver.Resolve(null, "en, fr").ShouldBe("en");
    }

    [Fact]
    public void AnHugeOrHostileHeader_NeverBreaksAnything()
    {
        var huge = string.Join(",", Enumerable.Repeat("de;q=0.5", 20_000)) + ",fr;q=0.9";

        Should.NotThrow(() => LanguageResolver.Resolve(null, huge));
        LanguageResolver.Resolve(null, new string(',', 100_000)).ShouldBe("en");
    }

    [Fact]
    public void TheSupportedLanguages_AreEnglishFirst_WithTheirOwnNames()
    {
        SupportedLanguages.All.Select(language => language.Code).ShouldBe(["en", "fr"]);
        SupportedLanguages.Default.Code.ShouldBe("en");
        SupportedLanguages.All.Single(language => language.Code == "fr").NativeName.ShouldBe("Français");
        SupportedLanguages.All.Single(language => language.Code == "en").NativeName.ShouldBe("English");
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("FR", true)]
    [InlineData(" en ", true)]
    [InlineData("de", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("fr-FR", false)]
    public void TryFind_RecognizesOnlyTheSupportedCodes(string? code, bool expected)
    {
        SupportedLanguages.TryFind(code, out var language).ShouldBe(expected);
        (language is not null).ShouldBe(expected);
    }
}
