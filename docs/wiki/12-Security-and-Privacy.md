# 12 — Security & Privacy

**This is the page to hand to the school board, the IT department or the
data protection officer (*dataskyddsombud*).**

It answers, in order: what data exists, where it is kept, who can reach it, what
happens if it leaves the computer, and what the school has to decide.

> **Note on scope.** This page describes what the software does and gives the
> school the facts it needs. It is not legal advice. The **school (or the
> municipality) is the data controller** and makes the final call, normally with
> its data protection officer, which every Swedish public school is required to
> have. Everything below is written so that conversation can be short.

---

## 1. Summary for a busy reader

| Question | Answer |
|---|---|
| What personal data does the café system hold? | A student's **name**, optionally their **class**, their **café balance**, and what they bought in the café |
| Anything sensitive? | **No.** No personal identity number, no address, no phone, no email, no photo, no health, allergy or dietary information, no card or bank details, no marks, no attendance |
| Where is it? | One file on one school computer, in the café |
| Does it go to the internet? | **Only if the school switches on cloud backup**, and then only as an **AES-256 encrypted** file the provider cannot read |
| Does it need the internet to work? | No. The till runs entirely offline |
| Is there a website? | Optionally. A student site **on the school network only**, off until the school turns it on — see [§5b](#5b-the-student-site) |
| Are there user accounts or passwords? | **The café stores no password of any kind.** The till has one local admin PIN (stored as a hash). Students sign in to the site with their existing school Google or Microsoft account, at the provider |
| Does any data go to the developer or any third party? | **No.** There is no vendor server, no telemetry, no analytics, no crash reporting to anyone |
| Can a student's data be deleted on request? | Yes — see [Erasure](#7-the-rights-of-students-and-parents) |
| Who can see who bought what? | Whoever has the admin PIN, on that computer |
| Payment data? | None. The system records that money arrived; the actual payment happens in Swish, entirely outside this program |

**The two things the school must actually decide:** whether backups may go to
Google Drive / OneDrive and who holds the encryption passphrase; and whether to
turn the student site on. Both are off or conservative by default.

---

## 2. Exactly what is stored

This is the complete list. It is short by design — the program cannot store more
than this, because there are no fields for anything else.

### About a student

| Field | Required? | Why it exists |
|---|---|---|
| First and last name | Yes | To find the right person at the counter |
| Class (e.g. `9B`) | No | To distinguish two students with the same name, and for reports per class |
| Balance | Yes | The purpose of the system |
| Credit limit | No | If one student is allowed a different floor |
| Active / inactive | Yes | To hide students who have left, without deleting their history |
| Note | No | Free text written by an admin, e.g. "cash only". **Staff are instructed not to write anything sensitive here**, and the field is shown at the till, which makes casual misuse visible |
| Created date, last activity date | Yes | Housekeeping |

### About what happened

Per transaction: date and time, the student, the amount, what was bought and at
what price, which till or admin recorded it, and — for deposits — the method
(`Swish` / `Cash`) and an optional reference the café types in from its own Swish
report.

### About a sign-in to the website

Only if the school turns the site on, and only for students whose address an admin
registered: the **school email address**, which **provider** it belongs to, that provider's
**opaque account id**, and the times the link was made, first used, and last used.

No password. No password hash. No token that could be used to sign in anywhere. Nothing
read from the account beyond the address that identifies it.

### What is *not* stored, at all

Personal identity number (*personnummer*), address, telephone, email, guardian
details, photograph, allergies or dietary requirements, health data, school
marks, attendance, IP addresses, device identifiers, card numbers, bank account
numbers, Swish phone numbers, and any Swish payload beyond a reference string the
café chooses to type in.

There is no field for any of it. Adding one would be a code change, visible in
this repository's history.

### A note on what purchase history is

A list of café purchases is ordinary personal data, not a special category under
GDPR Article 9 — the café sells sandwiches, not medicine. It is, however, data
about children, who deserve extra care (recital 38). The system's answers to that
are: store the minimum, keep it on one machine, put every change behind an
audit log, and give the school straightforward ways to delete it. If the café
ever starts recording *why* someone eats something — a gluten-free line item that
reveals a condition, for instance — that changes the analysis, and the school
should talk to its DPO first.

---

## 3. Where the data lives

```
                        ┌───────────────────────────────┐
                        │  Student phones and laptops   │
                        │  ON THE SCHOOL NETWORK ONLY   │
                        └───────────────┬───────────────┘
                                        │ HTTPS, sees only
                                        │ the signed-in student's
                                        │ own balance
   ┌────────────────────────────────────┴─────────┐
   │  The café computer (school property, in the  │
   │  café, physically in the school)             │
   │                                              │
   │   C:\ProgramData\CashCafe\cashcafe.db   ←──  │  the only live copy
   │   C:\ProgramData\CashCafe\Backups\      ←──  │  local backups
   │   Windows Credential Manager            ←──  │  PIN hash, cloud token
   │   CashCafe.Web (optional, off by default)    │  the student site
   └───────────────────┬──────────────────────────┘
                       │  optional, off by default
                       │  AES-256 encrypted file, one-way
                       ▼
   ┌──────────────────────────────────────────────┐
   │  Backup destination chosen by the school:    │
   │  school file server / USB / Google Drive /   │
   │  OneDrive                                    │
   └──────────────────────────────────────────────┘
```

The student site adds no new copy of anything: it reads the same database file on the
same computer, and it is not reachable from outside the school network.

There is **no other copy and no other destination**. Specifically:

- No server operated by the developer. None exists.
- No telemetry, usage statistics, analytics or crash reporting. The program makes
  **no outbound network connection at all** except: (a) an upload to a backup
  destination the school configured, and (b) an update check, which can be
  switched off and which sends nothing but a version number.
- No advertising or third-party SDKs of any kind.
- Application logs stay on the computer, are kept 30 days, and contain student
  **ids**, not names.

---

## 4. Who can do what

| | Café screen (the till) | Student site | Staff page on the site | Admin panel |
|---|---|---|---|---|
| Sell items, take deposits | ✔ | ✘ | ✘ | ✔ |
| Undo own purchase (15 min) | ✔ | ✘ | ✘ | ✔ |
| See a balance | ✔ (of the selected student) | ✔ **their own only** | ✘ | ✔ |
| See a student's purchase history | ✘ | ✔ **their own only** | ✘ | ✔ |
| See today's café totals | ✘ | ✘ | ✔ | ✔ |
| Set how busy the café is | ✘ | ✘ | ✔ | ✔ |
| Change prices or items | ✘ | ✘ | ✘ | ✔ |
| Correct a balance | ✘ | ✘ | ✘ | ✔ (with a written reason) |
| Export data | ✘ | ✘ | ✘ | ✔ |
| Change settings, backups | ✘ | ✘ | ✘ | ✔ |
| Delete anything | ✘ | ✘ | ✘ | ✘ — nobody can. Corrections are additions |

**Nothing on the website can move money.** It is a window onto the ledger, plus one
setting — how busy the café is — that staff can change and that says nothing about anyone.

**Why the till has no login:** the counter is staffed by rotating student
volunteers during a five-minute break. A password there would be shared,
written on a note, and useless. Instead, the till can do only harmless things,
and every action it takes is logged against the session. Everything with
consequences is behind the PIN.

**The admin PIN:**

- 4–12 digits, chosen at first run.
- Stored only as a **PBKDF2-HMAC-SHA256 hash, 310 000 iterations, random
  16-byte salt** in Windows Credential Manager (DPAPI-protected). It is not in
  the database, not in a settings file, and not in any backup.
- Five failures locks admin for five minutes; every failure is logged.
- The panel auto-locks after 10 minutes idle.

**Physical security is the real boundary.** Anyone with the Windows account on
that computer can copy the database file. That is true of every desktop program.
The mitigations the school controls are: keep the computer in a locked café or
office, use a Windows account with a password, and turn on **BitLocker** — which
we recommend and which most schools already deploy by policy. If BitLocker is on,
a stolen café computer is an inert brick rather than a data breach.

---

## 5. Cloud backup — the part the board usually asks about

Full mechanics are in [Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md). The
points that matter for a security assessment:

**1. It is off until the school turns it on.** The default is a local folder.

**2. Everything that leaves the computer is encrypted first.** AES-256-GCM,
key derived with PBKDF2-SHA256 (310 000 iterations, per-backup salt). The
passphrase never leaves the school. Google or Microsoft receive a file of
random-looking bytes; they can see its name, size and timestamp, and nothing
else. This means the cloud provider is not processing readable student data at
all — it is storing an opaque object.

**3. The permissions requested are the narrowest the platforms offer.**

| Provider | Scope requested | What that allows |
|---|---|---|
| Google Drive | `drive.file` | **Only files this program created.** It cannot list, read or search anything else in the Drive |
| OneDrive | `Files.ReadWrite.AppFolder` | **Only the program's own app folder.** The rest of OneDrive is invisible to it |

Broad scopes (`drive`, `Files.ReadWrite.All`) are not requested and the program
is not built to request them. If the stored token were somehow stolen, the
attacker would reach a folder of encrypted café backups — and would still need
the passphrase.

**4. Sign-in is the provider's own.** OAuth 2.0 with PKCE in the system browser.
The program never sees, handles or stores a Google or Microsoft password, and it
inherits the school tenant's MFA and conditional access rules automatically.
The resulting refresh token is stored with **Windows DPAPI**, decryptable only by
that Windows user on that machine.

**5. The school stays in control.** A Workspace or Entra administrator can see
the connected application and revoke it centrally at any time; the account owner
can revoke it themselves. Revocation stops backups; it never affects the running
café.

**6. Where the data physically sits.** The backups live in the school's own
Google Workspace or Microsoft 365 tenant — the same place the school already
keeps documents, mail and schedules, under agreements the school already has,
including its Data Processing Agreement and the providers' EU data-region
options. Introducing café backups does not introduce a new supplier, a new
contract or a new country. Because the file is encrypted with a key the provider
never sees, the transfer question is substantially narrower than for the school's
ordinary documents.

**7. The provider-free option is fully supported.** A school that would rather
not use any external cloud can back up to its own file server or an encrypted USB
stick and get the same protection. Nothing in the program requires the internet.

### If the board's question is "is cloud backup a security problem?"

The honest answer, and the one to give:

> The alternative to cloud backup is not "no risk" — it is **one copy of the
> café's records on one laptop**, which will eventually be dropped, stolen,
> wiped or infected. The realistic risk to student data here is losing it, or
> a stolen unencrypted laptop, not Google reading an encrypted archive.
>
> The design answers both: the data that leaves the building is encrypted with
> a key the school alone holds, and the app's access to the school's cloud is
> restricted to files it created itself. The residual decisions are the school's
> normal ones — who holds the passphrase, which account owns the backup folder,
> and is BitLocker on.

---

## 5b. The student site

Optional, **off until the school turns it on**, and reachable **only from the school
network**. Full description in [The Student Site](18-Student-Site.md); this is the part a
security review needs.

### What it is

A small web application running on the café computer, reading the same database the till
writes. A student signs in with their school account and sees **their own** balance, their
own café history, and how busy the café is. Staff get one extra page holding the busyness
slider and today's totals.

### What it changes about the risk picture

Being honest about this, because the answer before this feature existed was "there is no
server and no login, so there is nothing to attack":

| | Before | Now |
|---|---|---|
| Something listening on the network | No | Yes — on the school network only |
| Accounts | None | Sign-in via the school's existing Google/Microsoft accounts |
| Passwords stored by the café | None | **Still none** |
| Personal data leaving the café computer | Only encrypted backups | Only what a student sees about themselves, on the school network |
| Ways to change a balance | The till, behind the counter | **Unchanged — the site cannot move money** |

So it is a real addition to the attack surface, and the design answers it by making the
site as small as a thing can be: it reads one table, it writes one setting, it holds no
credentials, and it is not on the internet.

### The controls, specifically

**Authentication is not ours.** Sign-in happens at Google or Microsoft. The café never
sees a password, cannot leak one, and cannot have one stolen. The school's own MFA and
conditional access apply automatically, because it is the school's tenant doing the
authenticating.

**A school account is not, by itself, permission.** The site only recognises addresses an
admin deliberately registered against a student. Anyone else — including a real student
with a real school account the café has not registered — gets a polite sentence and no
data. The application requests no permission from the provider beyond "who is this":
no mail, no files, no directory, no groups.

**An address is bound to one account, once.** The first sign-in binds the registered
address to that account's stable id. A second account presenting the same address is
refused rather than being handed somebody's balance.

**The session is built from our own database.** Nothing the provider says becomes a
session on its own: the site looks the account up in its own table and issues its own
cookie holding a student id. The cookie is `HttpOnly`, `SameSite=Lax`, `Secure` on HTTPS,
and lasts 8 hours.

**There is nothing to tamper with.** No page, route, query string or form field on the
site names a student. The only student id it ever uses comes from the signed-in session,
so there is no parameter to change to see somebody else. There is an automated test that
tries exactly that and asserts it fails.

**Least privilege on the staff page.** Staff are ordinary signed-in users whose school
address appears in a configured list. The staff page can set the busyness and read today's
totals. It cannot change a price, correct a balance, see an individual's history, or
export anything — all of that stays in the till behind the admin PIN. A student who posts
directly to the staff page is refused; there is a test for that too.

**Ordinary web hardening.** Anti-forgery tokens on every form, rate limiting on sign-in,
`X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
`noindex`, and a Content-Security-Policy of `default-src 'self'` — which the site can
honestly set because it loads no scripts, styles or fonts from anywhere else at all.

**Turning it off is instant and total.** One setting. While it is off, every page — even
for someone already signed in — becomes a short explanation. The café keeps trading; the
site is never a dependency.

### What the school has to do

- Decide to turn it on at all. It works perfectly well switched off.
- Register the application in the school's own Google Workspace or Entra tenant.
- Give it an internal DNS name and a certificate from the school's own CA, and
  **run it over HTTPS** — on a school Wi-Fi network, plain HTTP means the session cookie
  travels readable.
- Keep `StaffEmails` short and to school addresses.
- Register students' addresses deliberately, and remove them when a student leaves.

### What it is not

Not reachable from home. Not a way to pay. Not a way for a guardian to see a child's
account. Not a way for anyone to see anyone else. Adding any of those would be a new
feature with its own assessment — see [Roadmap](15-Roadmap.md).

---

## 6. GDPR in practical terms

For the school's documentation. The wording is for the school to adopt or amend
with its DPO.

| Item | Suggested content |
|---|---|
| **Controller** | The school / the municipal school board |
| **Processor** | None for normal operation — the software runs locally and the developer receives nothing. If cloud backup is enabled, Google or Microsoft act as processors, under the school's existing agreement |
| **Purpose** | Administering a school café: keeping track of prepaid balances so students can buy food without cash |
| **Legal basis** | Normally **Article 6(1)(e)**, a task carried out in the public interest, or **6(1)(b)/(f)** for the café as a voluntary service. Consent is generally the wrong basis in a school setting because it is not freely given. **The school's DPO decides** |
| **Categories of data subject** | Students who choose to use the café; staff operating it (as the operator label on a transaction, and — where the site is used — the school address that moved the busyness slider) |
| **Categories of data** | Name, class, balance, café purchase history, deposits. Plus, for students using the site, their school email address and the provider's account id |
| **Special categories (Art. 9)** | **None** |
| **Recipients** | Nobody, unless cloud backup is enabled — then the school's own cloud provider, receiving encrypted files. If the student site is enabled, the school's own identity provider learns that the person signed in to a school application, which it already knows for every other school system |
| **Transfers outside the EU/EEA** | None by the software itself. If cloud backup is enabled, governed by the school's existing agreement with its provider and its choice of data region; the encryption means the provider holds no readable personal data |
| **Retention** | See below — a decision for the school |
| **Security measures** | Local storage, admin PIN (hashed), append-only audit log, encrypted backups, narrow cloud scopes, recommended full-disk encryption, physically secured computer |
| **Automated decision-making / profiling** | None |
| **DPIA** | A full DPIA is unlikely to be required at this scale and sensitivity, but the school should record that assessment. This page is written to be usable as the input to it |

### Retention — the school's decision

The program keeps everything indefinitely by default, because an accounting
record that silently disappears is worse than useless. A sensible policy, which
the program supports:

| Data | Suggested retention |
|---|---|
| Balances of active students | While the student attends the school |
| Purchase history | The current school year plus one, then anonymise |
| Records for a leaving student | Settle the balance, then anonymise at the end of that school year |
| Audit log | Same as purchase history |
| Backups | Rolling 14 daily / 8 weekly / 12 monthly (deleted automatically) |

**Anonymise** rather than delete: the student's name is replaced with
`Student #41` and the class is cleared, while the transaction rows stay. The
café's totals still add up, the accounts remain auditable, and the data is no
longer personal data. This is the recommended end-of-year action, and it is a
one-click bulk operation in **Admin → Students** (*planned for 1.1; until then it
is done per student*).

### If there is a breach

A lost laptop or a leaked backup would be assessed by the school as controller,
with a 72-hour notification duty to IMY if there is a risk to the individuals.
What makes this system's answer short: the data is a name and a café balance;
if BitLocker is on and backups are encrypted, a lost device generally does not
expose readable personal data. The audit log lets the school state exactly what
existed at the time.

---

## 7. The rights of students and parents

| Right | How it is served |
|---|---|
| **Access** — "what do you have about me?" | **Admin → Students → Statement** produces a complete PDF or Excel of everything about that person, in under a minute |
| **Rectification** | An admin correction with a written reason; the original stays visible, which is what makes the correction trustworthy |
| **Erasure** | Refund or settle the balance, then **anonymise** the student. The name is gone; the café's books still balance. Where an accounting obligation requires the record to be kept, anonymisation satisfies both |
| **Restriction / objection** | Deactivate the student — they disappear from the till and no new data is recorded |
| **Portability** | Export that student's data as `.xlsx`, `.csv` or `.json` |
| **Transparency** | The school should mention the café system in the information it already gives students and guardians about school systems. A short paragraph is enough; a suggested wording is below |

**Suggested wording for the school's information notice:**

> The school café uses a local computer program to keep track of prepaid
> balances. It stores your name, your class, your balance and what you have
> bought in the café. The information is kept on a school computer in the café
> and is used only to run the café. Encrypted backups are kept [on the school's
> server / in the school's Google Workspace]. You may ask to see the information
> about you, to have it corrected, or to have it removed when you no longer use
> the café. Contact [the responsible teacher] or the school's data protection
> officer at [address].

---

## 8. Payments — why Swish is not a risk here

The café's money arrives by Swish, the standard Swedish mobile payment app, sent
from a parent's phone to the café's number. **That happens entirely in Swish, on
the parent's phone, between their bank and the café's account.**

This program is not part of that transaction. It never touches a bank, a card,
an account number, a phone number or a payment API. Somebody at the café reads
the café's own Swish report and records "Astrid, 100 kr, reference X" as a
deposit — the same act as writing it in the old Excel sheet.

Consequences for a security review:

- **No payment credentials exist in this system**, so none can leak.
- **PCI DSS does not apply** — no card data is processed, stored or transmitted.
- The system is a **prepaid ledger**, the same category as a paper list of who
  has paid, and it holds no more financial power than that.

If automatic Swish reconciliation is added later ([Roadmap](15-Roadmap.md)) that
analysis changes and would need a fresh look, which is exactly why it is not in
version 1.

---

## 9. How the software itself is kept trustworthy

| Practice | Detail |
|---|---|
| **Open source code** | The whole program is in this repository. Anyone at the school can read exactly what it does. There is nothing to take on trust |
| **Small dependency list** | About ten well-known open-source libraries, all permissively licensed and listed in [Architecture](10-Architecture.md). Fewer dependencies, fewer things to patch |
| **No network code in the core** | Networking exists only in the optional backup module. The till and the database never open a socket |
| **Append-only ledger, enforced by the database** | Database triggers reject `UPDATE` and `DELETE` on transactions and on the audit log. Not a policy — a mechanism |
| **Every change logged** | Who, what, when, before and after. The log cannot be edited from anywhere in the program |
| **No dynamic code, no macros** | Imported spreadsheets are read as data. Macros in an `.xlsm` are ignored and never executed |
| **SQL injection** | All queries are parameterised. Input from a spreadsheet is data, never SQL |
| **Input validation** | Amounts are integers in öre with hard bounds; quantities 1–99; names length-limited; file paths canonicalised before use |
| **Updates** | Manual and visible. The program never silently downloads or executes anything |
| **Tested** | The money rules, the −10 kr floor and the import parser have automated tests that run on every build |

### USB sticks and portable mode

Portable mode puts the whole café — the database, unencrypted — on a USB stick.
It is genuinely useful for moving between rooms, and it is genuinely the easiest
way to lose the data to a corridor floor. If the school uses it: keep the stick
locked up, use a hardware-encrypted stick or BitLocker To Go, and make sure a
backup exists somewhere the stick is not.

---

## 10. The school's checklist

Nine things, and then this is signed off:

- [ ] The café computer has a **password-protected Windows account**.
- [ ] **BitLocker** (or equivalent full-disk encryption) is on.
- [ ] The computer is **physically secured** when the café is closed.
- [ ] The **admin PIN** is known by the responsible teacher, written down, and stored where the school keeps such things.
- [ ] A **backup destination** is chosen and tested — school server, USB or cloud.
- [ ] If cloud: the backup folder is owned by a **school account**, not a personal one, and backup **encryption is on**.
- [ ] The **encryption passphrase** is written down and at least two people know where.
- [ ] A **restore has been tested** once, on a spare computer.
- [ ] The café system is mentioned in the school's **information to students and guardians**, and a **retention decision** has been recorded.

If the **student site** is switched on, four more:

- [ ] It is served over **HTTPS** with a certificate school devices trust, on an internal name.
- [ ] It is reachable **only from the school network** — confirmed with school IT, not assumed.
- [ ] The application is registered in the **school's own** Google Workspace or Entra tenant, restricted to the school's organisation.
- [ ] `StaffEmails` contains only the people who actually run the café, and is reviewed each term.

## 11. Questions a board might ask, answered

**"Can a student hack it and give themselves money?"**
Not from the till: there is no field that sets a balance, and the floor is
checked inside the database write. With the admin PIN they could write a
correction — which is why the PIN is a teacher's, and why every correction is
permanently logged with a reason and shows up in the audit summary report.
Someone with full Windows access to that computer could edit the file with
external tools; that is true of any local file, and is why physical and Windows
account security is on the checklist above.

**"What if the computer breaks in the middle of the term?"**
Install the program on another Windows machine, restore the latest backup, set a
new PIN. A few minutes, and you are back to the last nightly backup at worst.

**"Who else can see what our students eat?"**
Nobody outside the school. There is no vendor, no server, no analytics. With
cloud backup enabled, the school's own provider stores an encrypted file it
cannot read.

**"What happens if the person who wrote this leaves the school?"**
The code, the documentation and the database format are all in this repository,
the data is in one SQLite file that dozens of free tools can read, and there is a
plain-CSV copy inside every backup. Nothing is locked to a person or a company.

**"Do we need a data processing agreement with the developer?"**
No, because the developer processes nothing — there is no service, no server and
no support access to the data. If the school later buys hosted support from
someone, that would need one.

**"Could a student see another student's balance on the website?"**
Not through the site. There is no page, link, or parameter that names a student — the
only student id it ever uses comes from the signed-in session, and there is an automated
test that tries to reach another student's data and asserts it cannot. A school account
the café has not registered sees nothing at all.

**"What if a student leaves their phone unlocked?"**
Whoever picks it up sees that student's café balance and what they bought, for up to
8 hours until the session expires. That is the honest answer, and it is the same exposure
as any other school system on that phone. Signing out ends it immediately, and an admin
can switch a student's login off at any time.

**"Do we have to have the website?"**
No. It ships switched off, the till does not need it, and a school that would rather not
run anything on the network can leave it off forever.

**"Is it more secure than the Excel sheet we use now?"**
Yes, and by a wide margin. The Excel sheet has no access control, no history, no
audit trail, no encryption, no controlled backups, and it is routinely emailed
around and copied to personal machines. This program keeps one controlled copy,
records every change permanently, encrypts anything that leaves the building,
and can prove what happened.
