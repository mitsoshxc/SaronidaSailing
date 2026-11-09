using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SaronidaSailing.Web.Pages.Dev;

public class CmsSandboxModel(IWebHostEnvironment environment) : PageModel
{
    public IActionResult OnGet() => environment.IsDevelopment() ? Page() : NotFound();
}
