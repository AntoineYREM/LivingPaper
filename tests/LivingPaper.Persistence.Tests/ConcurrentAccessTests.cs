using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Persistence.Tests;

// A book is written by the analysis while the page reads it to show the progress (PAPER-8). On Windows a file that is being
// read cannot be replaced unless the reader allows it: neither side may ever fail because of the other.
public sealed class ConcurrentAccessTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABookBeingWritten_CanBeReadAtTheSameTime_AndTheOtherWayRound()
    {
        var repository = new JsonBookRepository(_temp.BooksPath);
        var book = new Book(BookId.New(), "Le Malade imaginaire", "malade.pdf", DateTimeOffset.UtcNow);
        await repository.SaveAsync(book, Token);
        var writing = true;

        var writer = Task.Run(
            async () =>
            {
                try
                {
                    for (var step = 1; step <= 400; step++)
                    {
                        book.PartsDone = step;
                        await repository.SaveAsync(book, Token);
                    }
                }
                finally
                {
                    writing = false;
                }
            },
            Token);
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(
            async () =>
            {
                var reads = 0;
                while (Volatile.Read(ref writing))
                {
                    (await repository.GetAsync(book.Id, Token)).ShouldNotBeNull();
                    (await repository.ListAsync(Token)).Books.Count.ShouldBe(1);
                    reads++;
                }

                return reads;
            },
            Token)).ToList();

        await writer;
        (await Task.WhenAll(readers)).Sum().ShouldBeGreaterThan(0);
        (await repository.GetAsync(book.Id, Token))!.PartsDone.ShouldBe(400);
    }

    [Fact]
    public async Task TheStepsAndTheTextOfABook_AreTheSame()
    {
        var steps = new JsonAnalysisStepStore(_temp.BooksPath);
        var texts = new FileBookTextStore(Path.Combine(_temp.Path, "uploads"));
        var id = BookId.New();
        await texts.SaveAsync(id, "text", Token);
        var writing = true;

        var writer = Task.Run(
            async () =>
            {
                try
                {
                    for (var step = 1; step <= 200; step++)
                    {
                        await steps.SaveAsync(id, $"part:{step}", "notes", Token);
                        await texts.SaveAsync(id, "text " + step, Token);
                    }
                }
                finally
                {
                    writing = false;
                }
            },
            Token);
        var reader = Task.Run(
            async () =>
            {
                while (Volatile.Read(ref writing))
                {
                    await steps.GetAllAsync(id, Token);
                    (await texts.ReadAsync(id, Token)).ShouldNotBeNull();
                }
            },
            Token);

        await Task.WhenAll(writer, reader);
        steps.Dispose();
    }
}
