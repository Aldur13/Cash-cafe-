# Cash Café — Wiki

Complete documentation for the school café balance system.
If you just want to know which buttons to press, read the
[README](../../README.md) or [Quick Start](03-Quick-Start.md) instead.

This wiki is the **specification**: it describes exactly how the program behaves,
down to the rules, the file formats and the database. It is detailed enough that
the program can be built from it, audited against it, or handed to a different
developer later.

---

## Table of contents

### For the people running the café
| Page | What's in it |
|---|---|
| [01 — Overview](01-Overview.md) | What the program is, the problem with the Excel sheet, who uses it |
| [02 — Installation](02-Installation.md) | Requirements, install, first-run setup, where files live |
| [03 — Quick Start](03-Quick-Start.md) | Your first day, step by step |
| [04 — Café Screen (the till)](04-Cafe-Screen.md) | Student search, item rows, Execute, undo — every field explained |
| [05 — Admin Panel](05-Admin-Panel.md) | Items, students, deposits, reports, settings, audit log |
| [06 — Excel Import](06-Excel-Import.md) | Supported sheet layouts, name/amount parsing, preview, conflicts |
| [07 — Excel Export](07-Excel-Export.md) | Export formats, every column, scheduled exports |
| [08 — Balance Rules](08-Balance-Rules.md) | The -10 kr floor, deposits, corrections, refunds, rounding |
| [13 — Troubleshooting](13-Troubleshooting.md) | What to do when something looks wrong |
| [14 — Keyboard Shortcuts](14-Keyboard-Shortcuts.md) | Full key map for fast serving |
| [17 — FAQ](17-FAQ.md) | Short answers to common questions |
| [18 — The Student Site](18-Student-Site.md) | The website where students check their own balance, and the busyness slider staff set |

### For whoever maintains or builds it
| Page | What's in it |
|---|---|
| [09 — Data Model](09-Data-Model.md) | SQLite schema, every table and column, the ledger design |
| [10 — Architecture](10-Architecture.md) | Projects, layers, libraries, build & release |
| [15 — Roadmap](15-Roadmap.md) | What is in scope now, what comes later (Swish, multi-till) |
| [16 — Glossary](16-Glossary.md) | Terms used throughout this wiki |

### For the school board / IT
| Page | What's in it |
|---|---|
| [11 — Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md) | How Google Drive / OneDrive / network backup works, in full |
| [18 — The Student Site](18-Student-Site.md) | What the website exposes, how students sign in, and what it deliberately does not do |
| [12 — Security & Privacy](12-Security-and-Privacy.md) | **The document to hand the school board.** What data exists, where it goes, GDPR, encryption, retention, and why cloud backup is not a security problem |

---

## Where the project is

The **domain, database and student site are built and tested**; the WPF till and admin
panel are specified here and not yet written. Every page still describes the finished
system — where something is not built yet, it says so.

## The one-paragraph summary

Cash Café is an offline Windows desktop program. It keeps a list of students, a
list of things the café sells, and an **append-only ledger** of every deposit and
every purchase. A student's balance is never stored as a number that someone
types — it is always the sum of their ledger rows, which is what makes it
impossible for the balance to drift the way a hand-edited Excel sheet does. The
person at the counter uses a single screen: search a student, pick up to ten
items, press Execute. Administrators get a separate PIN-protected panel for
items, prices, reports, import/export and backups. Balances are hard-limited to
**-10 kr**. All data lives in one SQLite file on one school computer; backups can
optionally be copied — encrypted — to a school Google Drive or OneDrive.

## Conventions used in this wiki

- **kr** means Swedish kronor (SEK). Money is stored internally in **öre**
  (1 kr = 100 öre) as whole numbers.
- Screens are named as they appear in the program: **Café**, **Admin**, **Deposit**.
- `Fixed width` means a literal file name, column name, key press or database name.
- Anything marked **planned** is not part of the first version.

---

## Publishing these pages to the GitHub Wiki tab

The pages live in the repository (`docs/wiki/`) so they are versioned together
with the code and reviewed in the same pull requests. To also show them under the
repository's **Wiki** tab:

```bash
# once: enable the Wiki in the repository settings, and create the first page
git clone https://github.com/Aldur13/Cash-cafe-.wiki.git
cp docs/wiki/*.md Cash-cafe-.wiki/
cd Cash-cafe-.wiki && git add . && git commit -m "Sync wiki from docs/wiki" && git push
```

`Home.md` becomes the wiki landing page automatically. If the pages are synced
this way, treat `docs/wiki/` as the original and the wiki as a copy — editing a
page in the GitHub wiki UI creates a version that the next sync will overwrite.
