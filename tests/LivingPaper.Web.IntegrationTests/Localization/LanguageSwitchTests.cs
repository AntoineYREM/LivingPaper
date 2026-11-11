using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests.Localization;

// The visitor's language, from the browser or from their choice, on the real application (PAPER-13).
public partial class LanguageSwitchTests
{
    private const string FrenchTagline = "Discutez avec les personnages de vos livres.";
    private const string EnglishTagline = "Chat with the characters of your books.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> GetAsync(HttpClient client, string path, string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await client.SendAsync(request, Token);
        return await response.Content.ReadAsStringAsync(Token);
    }

    private static async Task<HttpResponseMessage> SwitchAsync(HttpClient client, string code, string? returnUrl = null, bool withToken = true)
    {
        var token = withToken ? AntiforgeryToken().Match(await GetAsync(client, "/")).Groups[1].Value : string.Empty;
        var form = new Dictionary<string, string> { ["code"] = code };
        if (returnUrl is not null)
        {
            form["returnUrl"] = returnUrl;
        }

        if (withToken)
        {
            form["__RequestVerificationToken"] = token;
        }

        return await client.PostAsync("/Language", new FormUrlEncodedContent(form), Token);
    }

    // ---- From the browser ----

    [Fact]
    public async Task ByDefault_ThePageIsInEnglish()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await client.GetAsync("/", Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        html.ShouldContain("<html lang=\"en\"");
        html.ShouldContain(EnglishTagline);
        response.Content.Headers.ContentLanguage.ShouldContain("en");
    }

    [Fact]
    public async Task AFrenchBrowser_GetsAFrenchPage_WithTheMatchingLanguageAttributeAndHeader()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en;q=0.8");

        using var response = await client.SendAsync(request, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        html.ShouldContain("<html lang=\"fr\"");
        html.ShouldContain(FrenchTagline);
        html.ShouldNotContain(EnglishTagline);
        response.Content.Headers.ContentLanguage.ShouldContain("fr");
    }

    [Fact]
    public async Task ABrowserInAnotherLanguage_GetsEnglish()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        (await GetAsync(client, "/", "de-DE,de;q=0.9")).ShouldContain(EnglishTagline);
    }

    [Fact]
    public async Task ThePageSaysItVariesWithTheBrowserLanguageAndTheCookie_SoNoCacheMixesLanguages()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await client.GetAsync("/", Token);

        var vary = string.Join(",", response.Headers.Vary);
        vary.ShouldContain("Accept-Language");
        vary.ShouldContain("Cookie");
    }

    // ---- The visitor's choice ----

    [Fact]
    public async Task TheSwitcher_OffersEveryLanguage_WithItsOwnName_AndMarksTheCurrentOne()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var html = await GetAsync(client, "/", "fr");

        html.ShouldContain("method=\"post\"");
        html.ShouldContain("action=\"/Language\"");
        html.ShouldContain("__RequestVerificationToken");
        Regex.IsMatch(html, @"<button[^>]*value=""fr""[^>]*aria-current=""true""[^>]*>FR</button>|<button[^>]*aria-current=""true""[^>]*value=""fr""[^>]*>FR</button>").ShouldBeTrue("the current language is marked");
        var decoded = WebUtility.HtmlDecode(html); // Razor writes "ç" as an HTML entity.
        decoded.ShouldContain("aria-label=\"Français\"");
        decoded.ShouldContain("aria-label=\"English\"");
        html.ShouldContain("lang=\"en\"");
        Regex.Count(html, "aria-current=\"true\"").ShouldBe(1);
    }

    [Fact]
    public async Task Choosing_French_SetsAnHonestFunctionalCookie_AndGoesBackToThePage()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await SwitchAsync(client, "fr", "/");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("lp_lang=", StringComparison.Ordinal));
        cookie.ShouldStartWith("lp_lang=fr");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldContain("expires=", Case.Insensitive);
        (await GetAsync(client, "/")).ShouldContain(FrenchTagline); // The client keeps the cookie.
    }

    [Fact]
    public async Task TheChoice_BeatsTheBrowserLanguage()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        (await SwitchAsync(client, "fr")).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await GetAsync(client, "/", "en-US,en;q=0.9")).ShouldContain(FrenchTagline);
    }

    [Fact]
    public async Task Choosing_English_AfterFrench_GoesBack()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        await SwitchAsync(client, "fr");

        await SwitchAsync(client, "en");

        (await GetAsync(client, "/", "fr")).ShouldContain(EnglishTagline);
    }

    [Fact]
    public async Task AnUnknownLanguage_IsRefused_AndSetsNothing()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await SwitchAsync(client, "xx");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).ShouldNotContain(value => value.StartsWith("lp_lang=", StringComparison.Ordinal));
        (await GetAsync(client, "/")).ShouldContain(EnglishTagline); // Still English: nothing was remembered.
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await SwitchAsync(client, "fr", withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).ShouldNotContain(value => value.StartsWith("lp_lang=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Design?x=1", "/Design?x=1")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("http://evil.example/steal", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("evil", "/")]
    public async Task TheReturnAddress_MustBeLocal_ElseTheHomePageIsUsed(string returnUrl, string expected)
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await SwitchAsync(client, "fr", returnUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe(expected);
    }

    [Fact]
    public async Task OpeningTheSwitchAddressDirectly_JustGoesHome()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        using var response = await client.GetAsync("/Language", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task TheSwitcher_ReturnsToThePageTheVisitorWasOn()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var html = await GetAsync(client, "/?from=here");

        html.ShouldContain("name=\"returnUrl\"");
        html.ShouldContain("value=\"/?from=here\"");
    }

    // ---- What the page is made of ----

    [Fact]
    public async Task TheHomePage_IsBuiltWithTheSharedLayoutAndStyleSheets()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var html = await GetAsync(client, "/");

        html.ShouldContain("/css/tokens.css");
        html.ShouldContain("<main id=\"main\"");
        html.ShouldContain("class=\"skip-link\"");
        html.ShouldContain("Skip to the content");
        Regex.Count(html, @"<h1\b").ShouldBe(1);
    }

    [Fact]
    public async Task TheSkipLink_IsTranslatedToo()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        (await GetAsync(client, "/", "fr")).ShouldContain("Aller au contenu");
    }

    [Fact]
    public async Task TheHomePage_TextsAreEncoded_NeverTakenAsHtml()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        // A hostile cookie or header must not turn into markup.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/?x=<script>alert(1)</script>");
        request.Headers.TryAddWithoutValidation("Accept-Language", "<script>alert(1)</script>");
        using var response = await client.SendAsync(request, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        html.ShouldNotContain("<script>alert(1)</script>");
    }
}
