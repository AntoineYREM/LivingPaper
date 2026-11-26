using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Evals;

// Before a run that searches the passages: the chat of the application would fall back to the whole story without a word when the book
// is not indexed with the model, and the run would measure something else than asked. A phrase that is in no passage (a typing
// mistake) would be counted as never found.
internal static class PassageCheck
{
    public static async Task RunAsync(IPassageIndex index, Book book, string embeddingModel, QuestionSet questions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(questions);

        if (index.Check() is { } problem)
        {
            throw new EvalException($"The passages cannot be searched here ({problem}).");
        }

        var key = new BookKey(BookShelf.Library, book.Id.ToString());
        IReadOnlyList<StoredPassage> passages;
        try
        {
            var state = await index.GetStateAsync(key, cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                throw new EvalException($"The book {book.Id} has no passages in this folder: analyze it with a model for vectorization chosen.");
            }

            if (state.EmbeddingModel is null || !EmbeddingModelNames.Same(state.EmbeddingModel, embeddingModel))
            {
                throw new EvalException(
                    $"The passages of {book.Id} are not indexed with {embeddingModel} (they are with {state.EmbeddingModel ?? "no model"}).");
            }

            passages = await index.GetPassagesAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (PassageIndexException exception)
        {
            throw new EvalException(exception.Message);
        }

        var absent = questions.Items
            .Where(item => item.PassageQuotes?.Any(quote => !passages.Any(passage => PassageQuotes.Appears(quote, passage.Text))) == true)
            .Select(item => item.Id)
            .ToList();
        if (absent.Count > 0)
        {
            throw new EvalException($"A phrase of these questions is in no passage of the book: {string.Join(", ", absent)}.");
        }
    }
}
