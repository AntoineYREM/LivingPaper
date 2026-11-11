using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// What a visitor sees at an address where there is nothing: a book that was deleted, a mistyped address. The server runs this
// page again for every 404 (see Program.cs) and keeps the 404 status. It answers a POST too, because a form can lead to a
// 404 (deleting a book that is already gone), and has no form of its own to protect.
[IgnoreAntiforgeryToken]
public class NotFoundModel : PageModel
{
    public void OnGet()
    {
    }

    public void OnPost()
    {
    }
}
