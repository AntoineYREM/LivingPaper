using System.Net;
using System.Text.RegularExpressions;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

// PAPER-57: the interface is in English only for now. French is kept in reserve: the languages offered come from the
// configuration (LANGUAGES), English alone by default, and the switch is hidden when there is nothing to switch to.
public partial class OfferedLanguagesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    // ---- The setting ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("en")]
    public void WithoutASetting_OnlyEnglishIsOffered(string? setting)
    {
        OfferedLanguages.Parse(setting).Languages.Select(language => language.Code).ShouldBe(["en"]);
    }

    [Theory]
    [InlineData("en,fr")]
    [InlineData(" fr , EN ")]
    [InlineData("fr")]
    [InlineData("fr,fr,en")]
    public void English_IsAlwaysOfferedFirst_AndEachLanguageOnce(string setting)
    {
        OfferedLanguages.Parse(setting).Languages.Select(language => language.Code).ShouldBe(["en", "fr"]);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en,zz")]
    [InlineData("../fr")]
    public void AnUnknownLanguage_StopsTheApplication_WithAClearMessage(string setting)
    {
        var failure = Should.Throw<ArgumentException>(() => OfferedLanguages.Parse(setting));

        failure.Message.ShouldContain("LANGUAGES");
        failure.Message.ShouldContain("en, fr");
    }

    [Fact]
    public void TheSwitch_IsOnlyUsefulWithAtLeastTwoLanguages()
    {
        OfferedLanguages.Parse("en").CanSwitch.ShouldBeFalse();
        OfferedLanguages.Parse("en,fr").CanSwitch.ShouldBeTrue();
    }

    // ---- Which language a visitor gets ----

    [Fact]
    public void WhenOnlyEnglishIsOffered_TheCookieAndTheBrowserAreIgnored()
    {
        var english = OfferedLanguages.Parse("en");

        LanguageResolver.Resolve("fr", "fr,en;q=0.5", english).ShouldBe("en");
        LanguageResolver.Resolve(null, "fr-FR", english).ShouldBe("en");
    }

    [Fact]
    public void WhenFrenchIsOffered_TheCookieAndTheBrowserCount()
    {
        var both = OfferedLanguages.Parse("en,fr");

        LanguageResolver.Resolve("fr", null, both).ShouldBe("fr");
        LanguageResolver.Resolve(null, "fr-FR", both).ShouldBe("fr");
    }

    // ---- The real application ----

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> GetAsync(HttpClient client, string path, string? acceptLanguage = null, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        using var response = await client.SendAsync(request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Token);
    }

    [Fact]
    public async Task ByDefault_TheSiteIsInEnglish_WhateverTheBrowserOrAnOldCookieSays_WithoutASwitch()
    {
        using var factory = LivingPaperFactory.With(languages: null);
        using var client = NewClient(factory);

        var html = await GetAsync(client, "/", acceptLanguage: "fr-FR,fr;q=0.9", cookie: "lp_lang=fr");

        html.ShouldContain("<html lang=\"en\">");
        html.ShouldContain("Chat with the characters of your books.");
        html.ShouldNotContain("Discutez avec les personnages");
        html.ShouldNotContain("class=\"language\"");
        html.ShouldNotContain("name=\"code\"");
    }

    [Fact]
    public async Task ByDefault_ChoosingFrench_IsRefused()
    {
        using var factory = LivingPaperFactory.With(languages: null);
        using var client = NewClient(factory);
        var token = AntiforgeryToken().Match(await GetAsync(client, "/Settings")).Groups[1].Value;

        using var response = await client.PostAsync("/Language", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["code"] = "fr", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token }), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task WhenFrenchIsAddedToTheSetting_TheSwitchComesBack_AndFrenchWorks()
    {
        using var factory = LivingPaperFactory.With(languages: "en,fr");
        using var client = NewClient(factory);

        var html = await GetAsync(client, "/", acceptLanguage: "fr");

        html.ShouldContain("<html lang=\"fr\">");
        html.ShouldContain("class=\"language\"");
        html.ShouldContain("Discutez avec les personnages de vos livres.");
    }
}
