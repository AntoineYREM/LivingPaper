using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// The examples: public-domain books already analyzed, shipped with the application so it can be tried without a key. They are
// read only: they open on the same pages as a book of the library, without what changes a book, and the only thing that can be
// done with one is to copy it into the library, where it becomes a book like any other.
public class ExamplesModel(BookService books, BookPresenter presenter, ITextLocalizer texts) : PageModel
{
    public IReadOnlyList<BookPresenter.Row> Rows { get; private set; } = [];

    // Set when an example could not be added: the sentence for the alert.
    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync(string? id)
    {
        if (!BookId.TryParse(id, out var exampleId))
        {
            return NotFound();
        }

        var result = await books.AddExampleAsync(exampleId, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            PageNotice.Set(TempData, PageNotice.ExampleAdded, result.Added!.Title);
            return RedirectToPage("/Books/Index", new { id = result.Added.Id.ToString() });
        }

        if (result.Rejection == ExampleRejection.NotFound)
        {
            return NotFound();
        }

        var example = (await books.FindAsync(exampleId, HttpContext.RequestAborted))?.Book;
        await LoadAsync();
        Error = texts.Format("examples.error.duplicate", new { title = example?.Title ?? string.Empty });
        return Page();
    }

    private async Task LoadAsync() =>
        Rows = [.. (await books.ListExamplesAsync(HttpContext.RequestAborted)).Books.Select(presenter.Describe)];
}
