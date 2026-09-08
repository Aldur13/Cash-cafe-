# 15 — Roadmap

What is being built, in what order, and what is deliberately left for later.

## Version 1.0 — the working café

Everything described in this wiki:

- [ ] Excel import with layout detection and a full preview
- [ ] Café screen: live student search, up to 10 item rows, live totals, Execute
- [ ] The −10 kr floor, enforced in the UI and in the database write
- [ ] Undo at the counter, admin reversals, admin corrections with reasons
- [ ] Deposits (manual, with method and reference) and bulk deposits
- [ ] Admin panel: items, prices, students, today, reports, import, export, backup, settings, audit log
- [ ] Excel and CSV export, PDF statements, scheduled exports
- [ ] Local, USB and network backups; encrypted Google Drive and OneDrive backups
- [ ] Restore, verification, and the integrity checks
- [ ] Swedish and English interface
- [ ] Installer and portable build

**Built so far:** everything on the list above except the installer, with 263 automated
tests — the money rules and the −10 kr floor, the append-only ledger and schema, the
repositories and the database verifier, Excel import and export, backups with encryption
and restore, the [student site](18-Student-Site.md), and the WPF till and admin panel.

**Not yet done, and honestly stated:**

| Piece | Where it stands |
|---|---|
| The installer and portable zip | Not written. `dotnet publish` produces a working build today |
| Native Google Drive / OneDrive destinations | Not written. Pointing a folder destination at a desktop sync folder covers the same ground and is what most schools should do anyway ([Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md#6-any-other-cloud-without-integration)) |
| The WPF screens, run | Compile- and XAML-verified only; nobody has clicked through them. The behaviour behind them is covered by tests, but the layout needs a pass on a real Windows machine |
| PDF statements | The Excel and CSV statement exports work; the PDF one is not written |
| Scheduled exports and the backup scheduler | The backup engine and export both work and are tested; nothing yet runs them on a timer |

**Definition of done:** the café runs a full week on it without opening Excel
once, and a restore has been tested on a second computer.

## Version 1.1 — the things you notice after a term of use

| Feature | Why |
|---|---|
| **One-click anonymisation** of a leaving student, and bulk end-of-year anonymisation | Makes the retention policy in [Security & Privacy](12-Security-and-Privacy.md#retention--the-schools-decision) a button rather than a chore |
| **Combo items** ("Toast + juice, 22 kr") | The most common request from any café |
| **Favourites row** — six large buttons for the top sellers | Faster than typing for the 80 % case |
| **Low balance list to print** | So students can be told before they hit the floor |
| **Receipt view** | An on-screen summary a student can look at, no printer needed |
| **Dark mode / high contrast** | Café screens are often in bright rooms |
| **Item stock counter** | Optional "we have 20 toasts today", counting down |

## Version 1.2 — hardware

| Feature | Notes |
|---|---|
| **Barcode scanner** | Cheap USB scanners act as a keyboard. Scanning an item code fills an item row. The data model already stores an item id, so this is mostly UI work |
| **Student cards** | The school's existing cards, if they carry a scannable id. Would add a card-id field to `students`, which needs a line in the school's privacy notice |
| **Receipt printer** | ESC/POS over USB |
| **Cash drawer** | Opened by the receipt printer, for cafés that also take cash |

## Version 2.0 — the two big ones

These are real projects, not features, and each needs its own decision.

### Automatic Swish reconciliation

Today a person reads the café's Swish report and types the deposits in.
Three possible levels, cheapest first:

1. **Import the Swish report file** — the café downloads its own report from the
   bank and imports it. No integration, no new supplier, no new risk. This is
   almost certainly the right answer for a school café, and is a natural 1.1/1.2
   feature rather than a 2.0 one.
2. **Swish Handel API** — a real merchant integration, requiring an agreement
   with the bank, a certificate, and a security review. It would put payment
   infrastructure inside the program, which is exactly what
   [Security & Privacy](12-Security-and-Privacy.md#8-payments--why-swish-is-not-a-risk-here)
   currently gets to say it does not do.
3. **QR codes per student** — the student scans a code that pre-fills the amount
   and a reference the program can match automatically. A good middle option.

**Recommendation: build 1, consider 3, do not build 2 without the school
explicitly asking and a fresh privacy assessment.**

### More than one till

The current design is deliberately one computer. Two tills selling at once
requires a shared database with real concurrency, which means one of:

- A **shared SQLite file on a school server** — the tempting option, and the
  wrong one. SQLite over SMB has well-known locking problems and is a good way to
  corrupt a database.
- A **small server component** (SQLite or PostgreSQL behind a local API, on a
  school machine) with the tills as clients. Correct, and a significant change:
  a service to run, secure, patch and back up, and a new conversation with the
  school board.
- **A second till in "queue" mode** — it records sales locally and merges them
  into the main database when it reconnects. Simpler, but merging means conflict
  rules, and conflict rules mean a student's balance can be refused after the
  fact, which breaks the promise the floor makes.

**Recommendation: stay on one till until the café genuinely cannot cope**, then
build the server component properly.

## Not planned

Honest "no"s, so nobody spends time on them:

| Idea | Why not |
|---|---|
| A **guardian**-facing app or website | Guardian accounts need identity checks tying an adult to a child, and a much larger privacy assessment. The *student* site now exists ([page 18](18-Student-Site.md)); extending it to parents is a different project |
| Reaching the student site from home | It would need public hosting, a public certificate, and a considerably bigger conversation with the school board. Students buy food at school |
| Cloud-hosted version of the whole system | Turns a local school tool into a service someone has to operate and secure forever |
| Mac and Linux versions | The café has a Windows computer. The domain and data layers are portable if that ever changes |
| Loyalty points, discounts, campaigns | A school café is not a shop |
| Automatic ordering or supplier integration | Far outside the problem |
| Photos of students at the till | Faster to recognise, but it turns a name-and-balance system into a biometric-adjacent one. Not worth it |

## Contributing

The project is deliberately small and readable so a student can work on it. If
you change anything in this list, or anything in
[Balance Rules](08-Balance-Rules.md) or
[Security & Privacy](12-Security-and-Privacy.md), update the wiki in the same
commit — the wiki is the specification, not a description written afterwards.

## Open questions for the school

- Which licence should the code be released under? (MIT is suggested — simple,
  permissive, and lets another school use it.)
- Who is the named owner of the café system in the school's system inventory?
- What is the retention decision ([Security & Privacy](12-Security-and-Privacy.md#retention--the-schools-decision))?
- Is there a budget for a code-signing certificate? It removes the SmartScreen
  warning at install.
