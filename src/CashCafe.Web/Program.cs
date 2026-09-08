using System.Security.Claims;
using CashCafe.Data;
using CashCafe.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(CafeSiteOptions.Section).Get<CafeSiteOptions>() ?? new CafeSiteOptions();
builder.Services.Configure<CafeSiteOptions>(builder.Configuration.GetSection(CafeSiteOptions.Section));
builder.Services.AddSingleton(options);

// The site reads the same database file the till writes. It is a reader in every sense
// except the busyness slider, which staff move from the staff page.
builder.Services.AddSingleton(new CafeDatabase(options.DatabasePath));
builder.Services.AddSingleton<StudentRepository>();
builder.Services.AddSingleton<LedgerRepository>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<StudentLoginRepository>();
builder.Services.AddSingleton<ReportRepository>();
builder.Services.AddSingleton<CafeSiteService>();

builder.Services.AddRazorPages(razor =>
{
    razor.Conventions.AuthorizeFolder("/");
    razor.Conventions.AllowAnonymousToPage("/SignIn");
    razor.Conventions.AllowAnonymousToPage("/SignedOut");
    razor.Conventions.AllowAnonymousToPage("/Problem");
    razor.Conventions.AllowAnonymousToPage("/Closed");
});

var authentication = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.Cookie.Name = "cashcafe.session";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;

        // Secure whenever the request itself is: the school may run this over plain HTTP on
        // the internal network, and a cookie marked Secure would then never be sent at all.
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        cookie.ExpireTimeSpan = options.SessionLength;
        cookie.SlidingExpiration = true;
        cookie.LoginPath = "/SignIn";
        cookie.LogoutPath = "/SignOut";
        cookie.AccessDeniedPath = "/Problem";
    })
    // A short-lived cookie that only carries the provider's answer from the redirect back to
    // our callback. The identity the site actually runs on is built by us, from our own
    // database, so nothing a provider says can become a session on its own.
    .AddCookie("External", external =>
    {
        external.Cookie.Name = "cashcafe.external";
        external.Cookie.HttpOnly = true;
        external.Cookie.SameSite = SameSiteMode.Lax;
        external.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        external.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

// Both providers are optional: a school on Google alone configures only Google.
var google = builder.Configuration.GetSection("Authentication:Google");
if (!string.IsNullOrWhiteSpace(google["ClientId"]))
{
    authentication.AddGoogle(g =>
    {
        g.ClientId = google["ClientId"]!;
        g.ClientSecret = google["ClientSecret"]!;
        g.CallbackPath = "/signin-google";
        g.SignInScheme = "External";
        g.SaveTokens = false;   // nothing from the provider is kept beyond the sign-in itself
    });
}

var microsoft = builder.Configuration.GetSection("Authentication:Microsoft");
if (!string.IsNullOrWhiteSpace(microsoft["ClientId"]))
{
    authentication.AddMicrosoftAccount(m =>
    {
        m.ClientId = microsoft["ClientId"]!;
        m.ClientSecret = microsoft["ClientSecret"]!;
        m.CallbackPath = "/signin-microsoft";
        m.SignInScheme = "External";
        m.SaveTokens = false;
    });
}

builder.Services.AddAuthorization(auth =>
{
    // The staff page is for the people who run the café, listed by school address in config.
    auth.AddPolicy("Staff", policy => policy.RequireAssertion(context =>
    {
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        return email is not null &&
               options.StaffEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
    }));
});

// One sign-in attempt per second per address is plenty for a person, and stops a script
// from working through a list of addresses to find which ones the café knows.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.AddFixedWindowLimiter("signin", window =>
    {
        window.PermitLimit = 10;
        window.Window = TimeSpan.FromMinutes(1);
        window.QueueLimit = 0;
    });
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddHsts(hsts => hsts.MaxAge = TimeSpan.FromDays(365));

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Problem");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    // The site loads nothing from anywhere else, so it can say so and mean it.
    headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// The master switch. While the site is off, nothing is served but an explanation —
// including to anyone already holding a session cookie.
app.Use(async (context, next) =>
{
    var settings = context.RequestServices.GetRequiredService<SettingsRepository>().Load();
    var path = context.Request.Path;

    if (!settings.StudentSiteEnabled &&
        !path.StartsWithSegments("/Closed") &&
        !path.StartsWithSegments("/css") &&
        !path.StartsWithSegments("/favicon.ico"))
    {
        context.Response.Redirect("/Closed");
        return;
    }

    await next();
});

app.MapRazorPages();
app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();

app.Run();

/// <summary>Exposed so the integration tests can start the site in memory.</summary>
public partial class Program;
