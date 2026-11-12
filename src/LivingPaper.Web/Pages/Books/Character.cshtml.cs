using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages.Books;

// The sheet of a character: what the analysis wrote (a short description, a personality profile) and a form to change it.
// The chat always uses the texts saved here. The texts are validated on the server, and a form that is refused is shown
// again with what was typed and the errors beside their fields.
public class CharacterModel(BookService books, ITextLocalizer texts) : PageModel
{
    public Book Book { get; private set; } = null!;

    public Character Character { get; private set; } = null!;

    // What the form holds: the saved texts, or what the user typed when it was refused.
    public CharacterDraft Typed { get; private set; } = new(null, null, null);

    // A sentence for the field that is wrong, by form field ("name", "shortDescription", "chatProfile").
    public IReadOnlyDictionary<string, string> FieldErrors { get; private set; } = new Dictionary<string, string>();

    public string? Notice { get; private set; }

    // The sheet of an example: it can be read and chatted with, not changed.
    public bool IsExample { get; private set; }

    // The profile as paragraphs: one per line of text.
    public IReadOnlyList<string> Paragraphs => [.. Character.ChatProfile.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public async Task<IActionResult> OnGetAsync(string? id, int position)
    {
        if (!await LoadAsync(id, position))
        {
            return NotFound();
        }

        Typed = new CharacterDraft(Character.Name, Character.ShortDescription, Character.ChatProfile);
        Notice = PageNotice.Take(TempData, texts);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? id, int position, string? name, string? shortDescription, string? chatProfile)
    {
        if (!await LoadAsync(id, position))
        {
            return NotFound();
        }

        var draft = new CharacterDraft(name, shortDescription, chatProfile);
        var result = await books.UpdateCharacterAsync(Book.Id, position, draft, HttpContext.RequestAborted);
        switch (result.Outcome)
        {
            case CharacterUpdateOutcome.Saved:
                PageNotice.Set(TempData, PageNotice.CharacterSaved, result.Character!.Name);
                return RedirectToPage(new { id, position });
            case CharacterUpdateOutcome.Invalid:
                Typed = draft;
                FieldErrors = result.Errors.ToDictionary(error => error.Field, Describe, StringComparer.Ordinal);
                return Page();
            default:
                return NotFound();
        }
    }

    private async Task<bool> LoadAsync(string? id, int position)
    {
        if (!BookId.TryParse(id, out var bookId))
        {
            return false;
        }

        var found = await books.FindAsync(bookId, HttpContext.RequestAborted);
        var book = found?.Book;
        IsExample = found?.IsExample ?? false;
        var character = book?.Characters.Find(candidate => candidate.Position == position);
        if (book is null || character is null || book.Status != BookStatus.Done)
        {
            return false;
        }

        Book = book;
        Character = character;
        return true;
    }

    private string Describe(FieldError error)
    {
        var message = error.Code switch
        {
            CharacterErrors.Required => texts["character.error.required"],
            CharacterErrors.TooLong => texts.Format("character.error.too_long", new { max = MaxLength(error.Field) }),
            CharacterErrors.InvalidCharacters => texts["character.error.invalid_characters"],
            CharacterErrors.Taken => texts["character.error.taken"],
            _ => texts["character.error.invalid_characters"],
        };
        return texts.Format("character.error.prefix", new { message });
    }

    public static int MaxLength(string field) => field switch
    {
        "name" => CharacterLimits.Name,
        "shortDescription" => CharacterLimits.ShortDescription,
        _ => CharacterLimits.ChatProfile,
    };
}
