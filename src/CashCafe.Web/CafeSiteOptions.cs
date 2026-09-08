namespace CashCafe.Web;

/// <summary>Everything the site reads from configuration. Bound from the "Cafe" section.</summary>
public sealed class CafeSiteOptions
{
    public const string Section = "Cafe";

    /// <summary>The café database. The same file the till uses.</summary>
    public string DatabasePath { get; set; } = @"C:\ProgramData\CashCafe\cashcafe.db";

    /// <summary>
    /// School addresses allowed to open the staff page and move the busyness slider.
    /// Staff are ordinary signed-in users; this list is the only thing that separates them.
    /// </summary>
    public string[] StaffEmails { get; set; } = Array.Empty<string>();

    /// <summary>How long a sign-in lasts before the student has to sign in again.</summary>
    public TimeSpan SessionLength { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// A development-only sign-in that skips Google and Microsoft entirely, so the site can be
    /// run and tested without provider credentials. Refused outside the Development environment,
    /// and refused on any address other than the loopback one.
    /// </summary>
    public bool EnableDevelopmentSignIn { get; set; }
}
