using System.Net;
using System.Text.RegularExpressions;
using LivingPaper.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// A file far larger than a page accepts, so large that the page cannot read the request: the visitor is sent back to the
// form, which says why, instead of getting a bare error of the server (PAPER-88).
public partial class OversizedUploadTests
{
    private const string BookTooLarge = "This file is larger than 20 MB.";
    private const string ImportTooLarge = "This file is larger than a book of LivingPaper can be: 30 MB";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryToken();

    private static LivingPaperFactory NewFactory() =>
        LivingPaperFactory.With(configureServices: services => services.AddSingleton<IAnalysisScheduler>(new RecordingScheduler()));

    private static HttpClient NewClient(LivingPaperFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> PageAsync(HttpClient client, string path, string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        using var response = await client.SendAsync(request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string page, int megabytes, bool withToken = true)
    {
        using var content = new MultipartFormDataContent();
        if (withToken)
        {
            var html = await client.GetStringAsync(page, Token);
            content.Add(new StringContent(AntiforgeryToken().Match(html).Groups[1].Value), "__RequestVerificationToken");
        }

        content.Add(new ByteArrayContent(new byte[megabytes * 1024 * 1024]), "file", "huge.bin");
        return await client.PostAsync(page, content, Token);
    }

    [Theory]
    [InlineData("/import", 32, ImportTooLarge)]
    [InlineData("/add", 26, BookTooLarge)]
    public async Task AFileTooLargeForTheRequest_BringsBackTheForm_WhichSaysWhy(string page, int megabytes, string message)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendAsync(client, page, megabytes);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(page + "?refused=tooLarge");
        var form = await PageAsync(client, response.Headers.Location.OriginalString);
        form.ShouldContain("Error: " + message);
        form.ShouldContain("type=\"file\"");
        form.ShouldContain("aria-invalid=\"true\"");
        (await factory.Services.GetRequiredService<BookService>().ListAsync(Token)).Books.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/import", 32)]
    [InlineData("/add", 26)]
    public async Task ItIsRefusedBeforeAnythingIsRead_EvenWithoutTheTokenOfTheForm(string page, int megabytes)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendAsync(client, page, megabytes, withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Theory]
    [InlineData("/import", 1)]
    [InlineData("/add", 1)]
    public async Task AFileOfAnOrdinarySize_StillNeedsTheTokenOfTheForm(string page, int megabytes)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        using var response = await SendAsync(client, page, megabytes, withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/import", ImportTooLarge)]
    [InlineData("/add", BookTooLarge)]
    public async Task TheFormSaysNothing_UnlessAFileWasRefused(string page, string message)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        (await PageAsync(client, page)).ShouldNotContain(message);
        (await PageAsync(client, page + "?refused=anything")).ShouldNotContain("Error:");
    }

    [Theory]
    [InlineData("/import", "Ce fichier dépasse la taille d’un livre de LivingPaper")]
    [InlineData("/add", "Ce fichier dépasse 20 Mo.")]
    public async Task TheReasonIsInTheLanguageOfTheVisitor(string page, string message)
    {
        using var factory = NewFactory();
        using var client = NewClient(factory);

        (await PageAsync(client, page + "?refused=tooLarge", "fr")).ShouldContain(message);
    }
}
