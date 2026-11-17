using System.Net;
using LivingPaper.Domain;
using LivingPaper.Persistence;
using Shouldly;

namespace LivingPaper.Web.IntegrationTests;

// 0.3.0: a library made before the readable addresses is migrated when the application starts.
public class BookAddressMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABookWithAnOldIdentifier_IsOpenedAtTheAddressOfItsTitle_AtTheFirstStart()
    {
        using var factory = new LivingPaperFactory();
        var old = new Book(BookId.New(), "Cyrano de Bergerac", "c.epub", DateTimeOffset.UtcNow.AddDays(-2))
        {
            Status = BookStatus.Done,
            Summary = "France, 1640.",
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        };
        old.Characters.Add(new Character(1, "Cyrano", "A poet.", "I am Cyrano."));
        await new JsonBookRepository(Path.Combine(factory.DataDirectory, "books")).SaveAsync(old, Token);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var renamed = await client.GetAsync("/books/cyrano-de-bergerac", Token);
        using var gone = await client.GetAsync("/books/" + old.Id, Token);
        using var library = await client.GetAsync("/", Token);

        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await renamed.Content.ReadAsStringAsync(Token)).ShouldContain("Cyrano");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await library.Content.ReadAsStringAsync(Token)).ShouldContain("/books/cyrano-de-bergerac");
    }
}
