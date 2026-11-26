using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages.Books;

// The page of a book: its summary and its characters once the analysis is done, its progress (refreshed by itself) while it
// runs, and what went wrong, with Retry, when it failed. The chat button comes with the chat. When a model for vectorization
// is chosen, a finished book also says where its passages are, with a button to index them (PAPER-85); for an example, when
// it is shipped with passages. A book whose export holds passages warns about it (PAPER-88).
public class IndexModel(BookService books, BookPresenter presenter, ITextLocalizer texts) : PageModel
{
    public BookPresenter.Row Row { get; private set; } = null!;

    public Book Book => Row.Book;

    public IReadOnlyList<(string Label, string Value)> Facts { get; private set; } = [];

    public string? Notice { get; private set; }

    // Null when there is nothing to say: the feature is off, the book is not done, or an example has no passages.
    public BookPresenter.PassagesPanel? Passages { get; private set; }

    // What the export of the book will give.
    public ExportDescription Export { get; private set; } = new(false, ExportOmission.None);

    // The characters, the most important first.
    public IReadOnlyList<Character> Characters => [.. Row.Book.Characters.OrderBy(character => character.Position)];

    // An example is read only: nothing on its page changes it, and the only thing to do is to copy it into the library.
    public bool IsExample { get; private set; }

    public bool IsDone => Row.Book.Status == BookStatus.Done;

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        Notice = PageNotice.Take(TempData, texts);
        if (IsDone)
        {
            Export = await books.DescribeExportAsync(Book.Id, HttpContext.RequestAborted);
        }

        return Page();
    }

    // Only the progress, for the page to refresh it while the analysis runs (see wwwroot/js/book.js).
    public async Task<IActionResult> OnGetProgressAsync(string? id) =>
        await LoadAsync(id) ? Partial("_BookProgress", Row) : NotFound();

    // Only the passages, for the page to refresh them while they are indexed.
    public async Task<IActionResult> OnGetPassagesAsync(string? id) =>
        await LoadAsync(id) && Passages is not null ? Partial("_BookPassages", Passages) : NotFound();

    // The state may have changed since the page was shown (two clicks, another tab): the page then just shows what it is.
    public async Task<IActionResult> OnPostReindexAsync(string? id)
    {
        if (!BookId.TryParse(id, out var bookId) || (await books.FindAsync(bookId, HttpContext.RequestAborted))?.Book is not { } book)
        {
            return NotFound();
        }

        if (await books.ReindexAsync(bookId, HttpContext.RequestAborted))
        {
            PageNotice.Set(TempData, PageNotice.Indexing, book.Title);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRetryAsync(string? id)
    {
        if (!BookId.TryParse(id, out var bookId) || !await books.RetryAsync(bookId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        var book = await books.GetAsync(bookId, HttpContext.RequestAborted);
        PageNotice.Set(TempData, PageNotice.Retried, book?.Title ?? string.Empty);
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(string? id)
    {
        if (!BookId.TryParse(id, out var bookId))
        {
            return false;
        }

        var found = await books.FindAsync(bookId, HttpContext.RequestAborted);
        if (found is null)
        {
            return false;
        }

        var book = found.Book;
        IsExample = found.IsExample;
        Row = presenter.Describe(book);
        Facts = book.Status == BookStatus.Done ? presenter.Facts(book) : [];
        var view = IsExample
            ? await books.DescribeExamplePassagesAsync(book, HttpContext.RequestAborted)
            : await books.DescribePassagesAsync(book, HttpContext.RequestAborted);
        Passages = view is null ? null : presenter.Describe(book, view);
        return true;
    }
}
