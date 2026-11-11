using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages;

// The design reference page (PAPER-16): the visual identity with sample content, to be reviewed on a phone and on a
// computer. It exists in development only. In production it is not found, so its sample texts never reach a user
// and need no translation.
public class DesignModel(IWebHostEnvironment environment) : PageModel
{
    public IActionResult OnGet() => environment.IsDevelopment() ? Page() : NotFound();
}
