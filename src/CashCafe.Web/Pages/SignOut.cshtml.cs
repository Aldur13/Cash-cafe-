using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CashCafe.Web.Pages;

[AllowAnonymous]
public sealed class SignOutModel : PageModel
{
    public Task<IActionResult> OnGetAsync() => SignOutEverywhereAsync();

    public Task<IActionResult> OnPostAsync() => SignOutEverywhereAsync();

    private async Task<IActionResult> SignOutEverywhereAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync("External");
        return RedirectToPage("/SignedOut");
    }
}
