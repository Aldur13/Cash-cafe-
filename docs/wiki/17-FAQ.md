# 17 — FAQ

Short answers. Each links to the page with the long one.

### Do we need the internet?
No. The program is fully offline. The internet is only used if you switch on
cloud backup. → [Architecture](10-Architecture.md)

### Do students or parents need accounts?
Not for the café itself. If the school turns the optional website on, students sign in
there with the **school account they already have** — the café never stores a password.
Parents have no account. → [The Student Site](18-Student-Site.md)

### Can students check their balance on their phone?
Yes, if the school turns the website on. It works on the school network only.
→ [The Student Site](18-Student-Site.md)

### How do students know if there's a queue?
Staff drag a slider from Closed to Packed, and it shows on every student's page.
→ [The Student Site](18-Student-Site.md#the-busyness-slider)

### Can a student go below -10 kr?
Not through a purchase — the program refuses. An admin can raise that one
student's limit, or record a correction, and both are logged.
→ [Balance Rules](08-Balance-Rules.md#the-minimum-balance)

### Can I still use my Excel sheet?
You can import it, and you can export back to Excel whenever you like. But once
you have imported it, stop editing the old sheet — the program is the record now.
→ [Excel Import](06-Excel-Import.md)

### How many items can one purchase have?
Ten rows, each with a quantity up to 99. Need more? Do a second purchase.
→ [Café Screen](04-Cafe-Screen.md#2-the-item-rows)

### What if I make a mistake?
`Ctrl+Z` within 15 minutes. Later than that, an admin reverses it. Nothing is
ever deleted; the fix is recorded alongside the mistake.
→ [Balance Rules](08-Balance-Rules.md#corrections-and-reversals)

### Can two people use it at the same time on two computers?
Not in version 1. One computer, one till. → [Roadmap](15-Roadmap.md#more-than-one-till)

### Does it connect to Swish?
No. Someone reads the café's own Swish report and records the deposits. That is
deliberate — it keeps payment infrastructure out of the program entirely.
→ [Security & Privacy](12-Security-and-Privacy.md#8-payments--why-swish-is-not-a-risk-here)

### Is putting our data in Google Drive safe?
Backups are encrypted with AES-256 before they leave the computer, using a
passphrase the school holds, and the program can only see files it created
itself. Google stores an unreadable file.
→ [Security & Privacy](12-Security-and-Privacy.md#5-cloud-backup--the-part-the-board-usually-asks-about)

### What personal data does it store?
Name, class, balance, and café purchases. Nothing else — no personal identity
number, no contact details, no health or allergy information.
→ [Security & Privacy](12-Security-and-Privacy.md#2-exactly-what-is-stored)

### A parent asks what we hold about their child. What do I do?
**Admin → Students → Statement** produces the complete record as a PDF.
→ [Security & Privacy](12-Security-and-Privacy.md#7-the-rights-of-students-and-parents)

### What if the computer dies?
Install on another Windows machine, restore the latest backup, set a new PIN.
Test this once a term so you know it works.
→ [Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md#restoring)

### Can I change prices without breaking old sales?
Yes. Every sale stores the price it was sold at. Old reports keep old prices.
→ [Balance Rules](08-Balance-Rules.md#price-changes)

### Two students have the same name.
Add their class. The till shows it, and the search matches on it too.
→ [Café Screen](04-Cafe-Screen.md#1-the-student-box-live-search)

### What happens at the end of the school year?
The school decides: refund, carry over, or write off with an adjustment. The
program gives you the reports to do it and the audit trail to explain it.
→ [Balance Rules](08-Balance-Rules.md#money-and-the-end-of-the-school-year)

### Can I try it without risking real data?
Yes — use the portable build on a USB stick with test data, or take a backup
first. Restoring puts everything back exactly as it was.
→ [Installation](02-Installation.md#portable-version)

### Can a student see someone else's balance on the site?
No. There is no page or parameter on the site that names a student — the only id it uses
comes from the signed-in session, and a test asserts that trying fails.
→ [Security & Privacy](12-Security-and-Privacy.md#5b-the-student-site)

### Where is our data, physically?
`C:\ProgramData\CashCafe\cashcafe.db` on the café computer. That one file is the
café. → [Installation](02-Installation.md#where-files-are-kept)
