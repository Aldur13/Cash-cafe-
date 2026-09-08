using System.Security.Claims;
using CashCafe.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CashCafe.Web.Pages;

public sealed class IndexModel(CafeSiteService cafe) : PageModel
{
    public StudentView View { get; private set; } = null!;
    public bool ShowBusyness { get; private set; }
    public BusynessLevel Busyness { get; private set; }
    public DateTimeOffset? BusynessUpdated { get; private set; }
    public bool IsStaff { get; private set; }

    public IActionResult OnGet()
    {
        // The only student id the site ever uses comes from the signed-in session. There is no
        // route or query parameter that names a student, so there is nothing to tamper with.
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(claim, out var studentId)) return SignOutAndReturn();

        var view = cafe.ForStudent(studentId);
        if (view is null) return SignOutAndReturn();

        View = view;

        var settings = cafe.Settings;
        ShowBusyness = settings.StudentSiteShowBusyness;
        Busyness = settings.Busyness;
        BusynessUpdated = settings.BusynessUpdatedUtc;

        return Page();
    }

    /// <summary>
    /// The student behind this session is gone or deactivated — end the session rather than
    /// showing a page built from nothing.
    /// </summary>
    private IActionResult SignOutAndReturn() =>
        RedirectToPage("/SignOut");
}
