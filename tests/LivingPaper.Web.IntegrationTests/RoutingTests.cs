using System.Net;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using LivingPaper.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// PAPER-59: the addresses are in lowercase, with plural resources (/books/{id}/characters/{position}/chat), and each page has a
// single address: the old ones and the ones with capitals are redirected for good.
public class RoutingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<Book> SeedAsync(LivingPaperFactory factory)
    {
        await factory.Services.GetRequiredService<BookAnalysisWorker>().Recovered;
        var book = new Book(BookId.New(), "Le Malade imaginaire", "malade.epub", DateTimeOffset.UtcNow.AddMinutes(-30))
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = BookStatus.Done,
            Summary = "France, 1673.",
            ProviderName = "OpenAI",
            Model = "gpt-5-nano",
        };
        book.Characters.Add(new Character(1, "Argan", "The imaginary invalid.", "I am Argan."));
        await factory.Services.GetRequiredService<IBookRepository>().SaveAsync(book, Token);
        return book;
    }

    private static async Task<(HttpStatusCode Status, string? Location, string Html)> GetAsync(HttpClient client, string path, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        using var response = await client.SendAsync(request, Token);
        return (response.StatusCode, response.Headers.Location?.OriginalString, await response.Content.ReadAsStringAsync(Token));
    }

    // ---- The new addresses ----

    [Theory]
    [InlineData("/")]
    [InlineData("/add")]
    [InlineData("/import")]
    [InlineData("/examples")]
    [InlineData("/settings")]
    public async Task ThePages_AnswerAtTheirLowercaseAddress(string path)
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        (await GetAsync(client, path)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ABook_ItsSheetsAndItsChat_AnswerAtTheirPluralAddresses()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);

        (await GetAsync(client, $"/books/{book.Id}")).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, $"/books/{book.Id}/characters/1")).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, $"/books/{book.Id}/characters/1/chat")).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, $"/books/{book.Id}/export")).Status.ShouldBe(HttpStatusCode.OK);
    }

    // ---- One address per page ----

    [Theory]
    [InlineData("/Add", "/add")]
    [InlineData("/Import", "/import")]
    [InlineData("/Examples", "/examples")]
    [InlineData("/Settings", "/settings")]
    [InlineData("/Settings?draft=abc", "/settings?draft=abc")]
    [InlineData("/EXAMPLES", "/examples")]
    public async Task AnAddressWithCapitals_IsRedirectedForGood_ToTheLowercaseOne(string path, string expected)
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var (status, location, _) = await GetAsync(client, path);

        status.ShouldBe(HttpStatusCode.MovedPermanently);
        location.ShouldBe(expected);
    }

    [Fact]
    public async Task TheOldSingularAddresses_AreRedirectedForGood_ToThePluralOnes()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);

        (await GetAsync(client, $"/Book/{book.Id}")).ShouldBe((HttpStatusCode.MovedPermanently, $"/books/{book.Id}", string.Empty));
        (await GetAsync(client, $"/Book/{book.Id}/Character/1")).Location.ShouldBe($"/books/{book.Id}/characters/1");
        (await GetAsync(client, $"/Book/{book.Id}/Character/1/Chat")).Location.ShouldBe($"/books/{book.Id}/characters/1/chat");
        (await GetAsync(client, $"/Book/{book.Id}/Export")).Location.ShouldBe($"/books/{book.Id}/export");
        (await GetAsync(client, $"/book/{book.Id}")).Location.ShouldBe($"/books/{book.Id}");
    }

    [Fact]
    public async Task APostIsNeverRedirected_SoAFormKeepsWorking()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        // Without the antiforgery token the post is refused by the page itself (400), not sent somewhere else.
        var (status, location, _) = await GetAsync(client, "/Settings", HttpMethod.Post);

        status.ShouldNotBe(HttpStatusCode.MovedPermanently);
        location.ShouldBeNull();
    }

    [Fact]
    public async Task TheUnknownPage_StillAnswers404_WithTheNotFoundPage()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);

        var (status, _, html) = await GetAsync(client, "/nothing-here");

        status.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("<html");
    }

    // ---- The links the pages write ----

    [Fact]
    public async Task TheLinksOfThePages_AreAllLowercase_AndPlural()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);

        var library = (await GetAsync(client, "/")).Html;
        var bookPage = (await GetAsync(client, $"/books/{book.Id}")).Html;
        var sheet = (await GetAsync(client, $"/books/{book.Id}/characters/1")).Html;
        var chat = (await GetAsync(client, $"/books/{book.Id}/characters/1/chat")).Html;

        library.ShouldContain($"href=\"/books/{book.Id}\"");
        library.ShouldContain("href=\"/add\"");
        library.ShouldContain("href=\"/import\"");
        library.ShouldContain("href=\"/examples\"");
        library.ShouldContain("href=\"/settings\"");
        library.ShouldContain($"href=\"/books/{book.Id}/export\"");
        bookPage.ShouldContain($"href=\"/books/{book.Id}/characters/1\"");
        bookPage.ShouldContain($"href=\"/books/{book.Id}/characters/1/chat\"");
        sheet.ShouldContain($"href=\"/books/{book.Id}\"");
        chat.ShouldContain($"href=\"/books/{book.Id}/characters/1\"");

        foreach (var html in new[] { library, bookPage, sheet, chat })
        {
            Regex.Matches(html, "(?:href|action|data-endpoint|data-refresh-url)=\"(/[^\"?#]*)").Select(match => match.Groups[1].Value)
                .Where(address => !address.Contains('.') && address.Any(char.IsUpper))
                .ShouldBeEmpty();
            html.ShouldNotContain("/Book/");
        }
    }

    // ---- PAPER-62: the way back ----

    [Fact]
    public async Task TheWayBackFromABook_LeadsToTheLibrary_NotToAnUnresolvedTilde()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);

        var html = (await GetAsync(client, $"/books/{book.Id}")).Html;

        html.ShouldContain("<p class=\"crumb\"><a href=\"/\">Back to the library</a></p>");
    }

    [Fact]
    public async Task NoPage_LeavesATildeInALink()
    {
        using var factory = new LivingPaperFactory();
        using var client = NewClient(factory);
        var book = await SeedAsync(factory);

        foreach (var path in new[] { "/", "/add", "/import", "/examples", "/settings", $"/books/{book.Id}", $"/books/{book.Id}/characters/1", $"/books/{book.Id}/characters/1/chat" })
        {
            Regex.IsMatch((await GetAsync(client, path)).Html, "(?:href|src|action)=\"~").ShouldBeFalse(path);
        }
    }
}
