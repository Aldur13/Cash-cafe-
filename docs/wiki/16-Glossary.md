# 16 — Glossary

| Term | Meaning |
|---|---|
| **Adjustment** | A transaction that changes a balance without a purchase or a deposit. Requires a written reason. The only way to correct a balance |
| **Append-only** | Rows can be added but never changed or removed. The ledger and the audit log are append-only, enforced by database triggers |
| **Audit log** | The permanent record of every action in the program — who, what, when, before and after |
| **Balance** | What a student has in the café. Always the sum of their transactions, never a typed number |
| **Café screen** | The till. The screen the café runs on all day |
| **Can spend** | Balance minus the floor. What the student may still spend before being refused |
| **Credit limit** | A per-student floor that overrides the café default |
| **DPAPI** | Windows Data Protection API. Encrypts secrets so only the same Windows user on the same machine can read them. Used for the PIN hash and cloud tokens |
| **DPIA** | Data Protection Impact Assessment. A GDPR document a controller writes when processing may be risky |
| **DPO / dataskyddsombud** | Data protection officer. Every Swedish public school must have one |
| **Execute** | The button that commits a purchase |
| **Floor / minimum balance** | The lowest a balance may go through purchases. Default −10,00 kr |
| **IMY** | *Integritetsskyddsmyndigheten*, the Swedish data protection authority |
| **Import id** | A tag on every row created by one import, so the whole import can be undone as a batch |
| **kr / SEK** | Swedish krona. The only currency in version 1 |
| **Ledger** | The `transactions` table. The complete, permanent list of everything that has happened to every balance. The source of truth |
| **Line** | One item row inside a purchase. Up to 10 per purchase |
| **OAuth 2.0 / PKCE** | The standard way a desktop program gets limited access to a cloud account without ever seeing the password |
| **öre** | 1/100 of a krona. Every amount is stored as a whole number of öre, so the arithmetic is exact |
| **Operator** | Who performed a transaction: `Café (till 1)` or `Admin` |
| **Portable mode** | Running from a USB stick, with the database next to the program |
| **Reversal** | A transaction that cancels an earlier one. Both stay visible. The correct way to undo |
| **Scope** (cloud) | What a cloud provider allows the program to touch. This program requests the narrowest available: its own files only |
| **Session** | One run of the program. Every transaction records the session that made it |
| **SQLite** | The single-file database the program uses |
| **Swish** | The Swedish mobile payment app parents use to send money to the café. It happens outside this program |
| **Transaction** | One row in the ledger: a purchase, deposit, adjustment, reversal or import |
| **WAL** | Write-ahead log. SQLite's crash-safety file; it lives next to the database and must be backed up with it |
