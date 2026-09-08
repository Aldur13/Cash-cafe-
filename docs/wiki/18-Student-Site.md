# 18 — The Student Site

A small website where a student signs in with their school account to see their own
balance, their own café history, and how busy the café is right now.

It runs **on the school network only**. It is a separate program from the till, reading
the same database file, and it can be switched off entirely from the admin panel without
affecting the café at all.

> **Note for the school board:** this page describes how it works. The decisions and the
> data-protection analysis are in [Security & Privacy](12-Security-and-Privacy.md), which
> was rewritten when this feature was added.

---

## What a student sees

One page:

- **Their balance**, large, with the amount they can still spend before the −10 kr limit.
- **How busy the café is** — five steps from Closed to Packed, set by staff with a slider,
  with a line telling them roughly what queue to expect.
- **Their café history** — date, what they bought, and the amount. A purchase that was
  undone appears struck through, so a student can see the mistake *and* the correction.

They cannot see anyone else's anything. There is no search, no list of other students, no
page that takes a student id, and nothing on the site can move money.

## What staff see

Staff — the people listed by school address in the site's configuration — get one extra
page at `/Staff`:

- **The busyness slider.** Drag it, and every student's page updates.
- **Today's totals** — sold, money in, purchases, and what sold. The same figures as the
  admin panel's Today tab, in the form you want on a phone behind the counter.

That is the whole staff page. Prices, corrections, the student list and the reports stay
in the till program behind the admin PIN, because those need a keyboard and a considered
decision, not a phone in a queue.

### The busyness slider

| Step | Shown to students | The line underneath |
|---|---|---|
| 0 | **Closed** | The café is closed right now. |
| 1 | **Quiet** | No queue — come now. |
| 2 | **Steady** | A short queue, a minute or two. |
| 3 | **Busy** | Busy — expect to wait a few minutes. |
| 4 | **Packed** | Very busy — come back later if you can. |

Details that matter in practice:

- The value is stored as a café setting, so the till's admin panel and the staff page
  are moving the same slider and always agree.
- **Every move is written to the audit log** with who moved it and what it was before.
  It is a small thing, but "who said we were closed at 11:30?" is a question that comes up.
- The slider saves when you let go of it, not on every step, so dragging from Closed to
  Packed writes one audit entry rather than four.
- If the café has been left on a busy setting for more than three hours, the staff page
  says so and asks whether it is still right. Nobody remembers to set it back to Closed.
- It works with JavaScript switched off — the **Set** button submits the same form.
- The whole indicator can be hidden from students with a setting, if the school would
  rather not show it.

---

## How students sign in

With their **school Google or Microsoft account** — the one they already have. The café
never sees, stores or handles a password.

The flow, in full:

1. The student opens the site and presses *Sign in with the school's Google account*.
2. Their browser goes to **Google's own sign-in page**. This is standard OAuth 2.0.
   Whatever the school's tenant requires — MFA, conditional access, a managed device — is
   applied there, automatically, because it is Google's sign-in and not ours.
3. Google sends the browser back with the account's stable subject id and email address.
4. The site looks that up in the café's own `student_logins` table:
   - **Registered and already bound to this account** → signed in.
   - **Registered but never used** → the account is bound to it now, once, permanently.
   - **Not registered** → *"No café account is connected to this address. Ask in the café
     and they can add it."* No balance, no hint about who else exists.
5. The site then builds its own session cookie from **its own database**, holding just the
   student id, name and address. Nothing Google says becomes a session on its own.

### Registering an address

An admin does this, per student, in the till program (or by importing a class list).
Until the café deliberately registers a school address, that person cannot see anything —
a valid school Google account is not by itself permission to see a café balance.

### Why binding on first use matters

The email address is what an admin types, but the **account** is what the site trusts
afterwards. So:

- If the school later renames the mailbox, the student is still signed in fine.
- If a different account presents the same address string, it is refused, because the
  address is already bound to somebody. It cannot be claimed twice.

Both cases are tested (`tests/CashCafe.Web.Tests`).

### Sessions

- A sign-in lasts **8 hours**, sliding, then it is asked for again.
- The cookie is `HttpOnly`, `SameSite=Lax`, and `Secure` whenever the request is HTTPS.
- **Sign out** ends it immediately.
- Deactivating a student, or switching their login off, ends their access on the next page
  they open — not whenever the cookie happens to expire.

---

## Setting it up

### 1. Turn the site on

**Admin → Settings → Student site.** It is **off** on a fresh installation.

| Setting | Default | What it does |
|---|---|---|
| Student site enabled | **Off** | The master switch. While off, every page redirects to a short explanation — including for anyone already signed in |
| Show purchase history | On | Off leaves only the balance |
| Show busyness | On | Off hides the indicator |
| History days | 90 | How far back a student can see. 0 means all of it |

### 2. Register the school as an application

Once, with whichever provider the school uses.

**Google Workspace** — in the Google Cloud console, create an OAuth client of type
*Web application*, restricted to the school's own organisation:

```
Authorised redirect URI:  https://cafe.skolan.internal/signin-google
```

**Microsoft 365** — in Entra ID, register an application with
*Accounts in this organizational directory only*:

```
Redirect URI:  https://cafe.skolan.internal/signin-microsoft
```

Neither needs any API permission beyond basic sign-in (`openid`, `email`, `profile`). The
site reads nothing from the account: no mail, no files, no directory, no groups. It asks
who you are and stops there.

### 3. Configure the site

`appsettings.json` next to the site, or better, **user secrets** so the client secret is
not in a file that gets copied:

```json
{
  "Cafe": {
    "DatabasePath": "C:\\ProgramData\\CashCafe\\cashcafe.db",
    "StaffEmails": [ "cafeteria@skolan.se", "lararen@skolan.se" ],
    "SessionLength": "08:00:00"
  },
  "Authentication": {
    "Google": { "ClientId": "…", "ClientSecret": "…" }
  }
}
```

`StaffEmails` is the entire difference between a student and a member of staff. Keep it
short, and keep it to school addresses.

### 4. Run it on the café computer

The site is a normal Windows service or scheduled task on the same computer as the till:

```
CashCafe.Web.exe --urls https://0.0.0.0:8443
```

It reads the same `cashcafe.db`. The till writes, the site reads — plus the busyness
slider, which is the one thing the site writes.

### 5. Give it a name and a certificate

Students will not type an IP address. Ask school IT for an internal DNS name
(`cafe.skolan.internal`) pointing at the café computer, and a certificate from the
school's internal certificate authority — which school-managed devices already trust, so
no browser warnings.

**Use HTTPS.** On a school Wi-Fi network, plain HTTP means the session cookie crosses the
air readable. The site works over HTTP and will not stop you, but do not do it.

---

## What the site deliberately does not do

| Not built | Why |
|---|---|
| Reachable from home | It would need real hosting, a public certificate, and a much larger conversation with the school board. Students buy food at school, so the site lives at school |
| Top up with Swish from the page | Payment infrastructure inside the program is exactly what [Security & Privacy](12-Security-and-Privacy.md#8-payments--why-swish-is-not-a-risk-here) currently gets to say does not exist |
| Order ahead / reserve a toast | Sounds useful, needs a queue, a fulfilment flow, and a way to cancel. A separate project |
| A page for guardians | Guardian accounts, identity checks, and a serious privacy assessment. Out of scope |
| Anything that changes a balance | The site is a window. Money moves at the till |

## For whoever maintains it

- **Project:** `src/CashCafe.Web`, ASP.NET Core 8 Razor Pages.
- **Tests:** `tests/CashCafe.Web.Tests` boots the real site against a temporary database
  and signs in through the site's own flow. The tests that matter are the ones asserting
  a student cannot reach another student's data and cannot move the slider.
- **No JavaScript framework, no CDN, no web fonts.** The site loads nothing from anywhere
  else, which is what lets its Content-Security-Policy be `default-src 'self'` and mean it.
- **Development sign-in:** `Cafe:EnableDevelopmentSignIn` skips the providers so the site
  can be run without credentials. It is refused unless the build is a Development one
  **and** the setting is on **and** the request came from the machine itself — all three,
  because any one of them alone is too easy to leave switched on by accident.
- **Trying it out:** `dotnet run --project tools/CashCafe.Demo -- demo.db` writes a café of
  invented students with a day of trading, and prints the addresses to sign in as.
