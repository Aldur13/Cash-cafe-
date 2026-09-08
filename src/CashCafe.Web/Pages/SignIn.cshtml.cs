using System.Security.Claims;
using CashCafe.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CashCafe.Web.Pages;

[AllowAnonymous]
[EnableRateLimiting("signin")]
public sealed class SignInModel(
    StudentLoginRepository logins,
    CafeSiteOptions options,
    IAuthenticationSchemeProvider schemes,
    IWebHostEnvironment environment,
    ILogger<SignInModel> log) : PageModel
{
    public bool GoogleAvailable { get; private set; }
    public bool MicrosoftAvailable { get; private set; }
    public bool DevelopmentSignInAvailable => options.EnableDevelopmentSignIn && environment.IsDevelopment();
    public string? Message { get; private set; }

    public async Task OnGetAsync(string? message)
    {
        Message = message;
        await LoadProvidersAsync();
    }

    /// <summary>Hands the browser to Google or Microsoft. We never see a password.</summary>
    public IActionResult OnPostChallenge(string provider) =>
        Challenge(new AuthenticationProperties { RedirectUri = Url.Page("/SignIn", "Callback") }, provider);

    /// <summary>
    /// The provider has answered. Everything from here is ours: we look the account up in the
    /// café's own table and build the session from that, so a valid Google account that the
    /// café has not registered gets a polite explanation rather than a balance.
    /// </summary>
    public async Task<IActionResult> OnGetCallbackAsync()
    {
        var external = await HttpContext.AuthenticateAsync("External");
        if (!external.Succeeded || external.Principal is null)
            return RedirectToPage("/SignIn", new { message = "That sign-in did not complete. Please try again." });

        var provider = external.Principal.Identity?.AuthenticationType?.ToLowerInvariant() ?? "google";
        var subject = external.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = external.Principal.FindFirstValue(ClaimTypes.Email);

        await HttpContext.SignOutAsync("External");

        if (subject is null || email is null)
            return RedirectToPage("/SignIn", new { message = "The school account did not give us an address to match." });

        return await CompleteAsync(NormalizeProvider(provider), subject, email);
    }

    /// <summary>
    /// Development-only sign-in, so the site can be run without provider credentials. It is
    /// refused unless the build is a Development one, the setting is on, and the request came
    /// from the machine itself — all three, because one of them alone is too easy to leave on.
    /// </summary>
    public async Task<IActionResult> OnPostDevelopmentAsync(string email)
    {
        if (!DevelopmentSignInAvailable || !IsLoopback())
        {
            log.LogWarning("Development sign-in refused for {Address}", HttpContext.Connection.RemoteIpAddress);
            return RedirectToPage("/SignIn", new { message = "That sign-in method is not available." });
        }

        return await CompleteAsync("google", $"dev-{email}", email);
    }

    private async Task<IActionResult> CompleteAsync(string provider, string subject, string email)
    {
        var result = logins.SignIn(provider, subject, email);

        if (!result.IsSignedIn || result.Student is null)
        {
            // Deliberately not logging the address: an unrecognised sign-in is somebody's
            // personal account, and the log is not the place for it.
            log.LogInformation("Sign-in refused: {Outcome}", result.Outcome);
            return RedirectToPage("/SignIn", new { message = result.Message });
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.Student.Id.ToString()),
            new Claim(ClaimTypes.Name, result.Student.DisplayName),
            new Claim(ClaimTypes.Email, email.ToLowerInvariant()),
        }, "cashcafe");

        await HttpContext.SignInAsync(
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        log.LogInformation("Student #{Id} signed in", result.Student.Id);
        return RedirectToPage("/Index");
    }

    private bool IsLoopback() =>
        HttpContext.Connection.RemoteIpAddress is null ||
        System.Net.IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress);

    private static string NormalizeProvider(string provider) =>
        provider.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ? "microsoft" : "google";

    private async Task LoadProvidersAsync()
    {
        var all = (await schemes.GetAllSchemesAsync()).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        GoogleAvailable = all.Contains("Google");
        MicrosoftAvailable = all.Contains("Microsoft");
    }
}
