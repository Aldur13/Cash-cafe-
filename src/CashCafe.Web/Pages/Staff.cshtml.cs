using System.Security.Claims;
using CashCafe.Data;
using CashCafe.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CashCafe.Web.Pages;

/// <summary>
/// The one page staff use from a phone: the busyness slider, and today's totals.
///
/// Everything with consequences — prices, corrections, the student list — stays in the till
/// program behind the admin PIN. Nothing here can move money.
/// </summary>
[Authorize(Policy = "Staff")]
public sealed class StaffModel(
    SettingsRepository settings,
    ReportRepository reports,
    ILogger<StaffModel> log) : PageModel
{
    public BusynessLevel Current { get; private set; }
    public DateTimeOffset? UpdatedUtc { get; private set; }
    public DaySummary Today { get; private set; } = null!;
    public bool Saved { get; private set; }
    public string StaffEmail => User.FindFirstValue(ClaimTypes.Email) ?? "staff";

    /// <summary>
    /// True when the slider has not been touched for a while. Nobody remembers to set the café
    /// back to Closed at the end of the day, so the page asks rather than assuming.
    /// </summary>
    public bool IsStale =>
        Current != BusynessLevel.Closed &&
        UpdatedUtc is not null &&
        DateTimeOffset.UtcNow - UpdatedUtc.Value > TimeSpan.FromHours(3);

    public void OnGet(bool saved = false)
    {
        Saved = saved;
        Load();
    }

    public IActionResult OnPost(int level)
    {
        if (level is < 0 or > 4)
        {
            Load();
            return Page();
        }

        var chosen = (BusynessLevel)level;
        settings.SetBusyness(chosen, StaffEmail, HttpContext.TraceIdentifier);
        log.LogInformation("Busyness set to {Level} by staff", chosen);

        return RedirectToPage("/Staff", new { saved = true });
    }

    private void Load()
    {
        var config = settings.Load();
        Current = config.Busyness;
        UpdatedUtc = config.BusynessUpdatedUtc;
        Today = reports.Day(DateOnly.FromDateTime(DateTime.Today));
    }
}
