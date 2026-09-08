-- Cash Café — initial schema.
--
-- The design rule behind all of it: the ledger is append-only and a balance is the sum
-- of its rows. The triggers at the bottom are what make that a mechanism rather than a
-- promise — no part of the application, and no accidental UPDATE, can rewrite history.

CREATE TABLE students (
    id                INTEGER PRIMARY KEY,
    first_name        TEXT    NOT NULL,
    last_name         TEXT    NOT NULL DEFAULT '',
    display_name      TEXT    NOT NULL,
    search_name       TEXT    NOT NULL,
    class_name        TEXT,
    balance_ore       INTEGER NOT NULL DEFAULT 0,
    credit_limit_ore  INTEGER,
    is_active         INTEGER NOT NULL DEFAULT 1,
    note              TEXT,
    merged_into_id    INTEGER REFERENCES students(id),
    created_utc       TEXT    NOT NULL,
    created_by        TEXT    NOT NULL,
    last_activity_utc TEXT
);
CREATE INDEX ix_students_search ON students(search_name);
CREATE INDEX ix_students_active ON students(is_active, display_name);

CREATE TABLE items (
    id            INTEGER PRIMARY KEY,
    name          TEXT    NOT NULL,
    search_name   TEXT    NOT NULL,
    category      TEXT,
    price_ore     INTEGER NOT NULL CHECK (price_ore >= 0),
    shortcut_key  TEXT,
    is_available  INTEGER NOT NULL DEFAULT 1,
    is_archived   INTEGER NOT NULL DEFAULT 0,
    sort_order    INTEGER NOT NULL DEFAULT 0,
    created_utc   TEXT    NOT NULL
);
CREATE UNIQUE INDEX ux_items_shortcut ON items(shortcut_key) WHERE shortcut_key IS NOT NULL;

CREATE TABLE price_history (
    id             INTEGER PRIMARY KEY,
    item_id        INTEGER NOT NULL REFERENCES items(id),
    price_ore      INTEGER NOT NULL,
    valid_from_utc TEXT    NOT NULL,
    valid_to_utc   TEXT,
    changed_by     TEXT    NOT NULL,
    reason         TEXT
);
CREATE INDEX ix_price_history_item ON price_history(item_id, valid_from_utc);

CREATE TABLE imports (
    id                   INTEGER PRIMARY KEY,
    file_name            TEXT    NOT NULL,
    archived_path        TEXT    NOT NULL,
    file_sha256          TEXT    NOT NULL,
    sheet_name           TEXT,
    mapping_json         TEXT    NOT NULL,
    rows_read            INTEGER NOT NULL,
    students_created     INTEGER NOT NULL,
    transactions_created INTEGER NOT NULL,
    total_ore            INTEGER NOT NULL,
    imported_utc         TEXT    NOT NULL,
    imported_by          TEXT    NOT NULL,
    undone_utc           TEXT
);

CREATE TABLE transactions (
    id                INTEGER PRIMARY KEY,
    student_id        INTEGER NOT NULL REFERENCES students(id),
    type              TEXT    NOT NULL
                      CHECK (type IN ('PURCHASE','DEPOSIT','ADJUSTMENT','REVERSAL','IMPORT')),
    amount_ore        INTEGER NOT NULL,
    balance_after_ore INTEGER NOT NULL,
    occurred_utc      TEXT    NOT NULL,
    recorded_utc      TEXT    NOT NULL,
    operator          TEXT    NOT NULL,
    session_id        TEXT    NOT NULL,
    method            TEXT,
    reference         TEXT,
    reason            TEXT,
    reverses_id       INTEGER REFERENCES transactions(id),
    import_id         INTEGER REFERENCES imports(id),
    note              TEXT,
    CHECK (type <> 'ADJUSTMENT' OR reason IS NOT NULL),
    CHECK (type <> 'REVERSAL'   OR (reason IS NOT NULL AND reverses_id IS NOT NULL)),
    CHECK (type <> 'DEPOSIT'    OR amount_ore > 0),
    CHECK (type <> 'PURCHASE'   OR amount_ore < 0)
);
CREATE INDEX ix_tx_student ON transactions(student_id, occurred_utc);
CREATE INDEX ix_tx_when    ON transactions(occurred_utc);
CREATE INDEX ix_tx_type    ON transactions(type, occurred_utc);
CREATE INDEX ix_tx_import  ON transactions(import_id) WHERE import_id IS NOT NULL;
-- One reversal per transaction, enforced by the database so a race cannot double-undo.
CREATE UNIQUE INDEX ux_tx_reverses ON transactions(reverses_id) WHERE reverses_id IS NOT NULL;

CREATE TABLE transaction_lines (
    id             INTEGER PRIMARY KEY,
    transaction_id INTEGER NOT NULL REFERENCES transactions(id),
    line_no        INTEGER NOT NULL CHECK (line_no BETWEEN 1 AND 20),
    item_id        INTEGER REFERENCES items(id),
    item_name      TEXT    NOT NULL,
    unit_price_ore INTEGER NOT NULL,
    quantity       INTEGER NOT NULL CHECK (quantity BETWEEN 1 AND 99),
    line_total_ore INTEGER NOT NULL,
    is_custom      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_lines_tx   ON transaction_lines(transaction_id);
CREATE INDEX ix_lines_item ON transaction_lines(item_id);

-- A student's link to a school Google or Microsoft account, for the website.
-- No password of any kind is stored: sign-in happens at the provider.
--
-- A row is created by an admin with the school email address and provider_subject NULL.
-- The first time that person signs in, the provider's stable subject id is written into
-- the row and the link is bound to that account for good. So only addresses the café has
-- deliberately registered can ever see a balance, and an address cannot be claimed twice.
CREATE TABLE student_logins (
    id               INTEGER PRIMARY KEY,
    student_id       INTEGER NOT NULL REFERENCES students(id),
    provider         TEXT    NOT NULL CHECK (provider IN ('google','microsoft')),
    provider_subject TEXT,
    email            TEXT    NOT NULL,
    linked_utc       TEXT    NOT NULL,
    linked_by        TEXT    NOT NULL,
    claimed_utc      TEXT,
    last_seen_utc    TEXT,
    is_enabled       INTEGER NOT NULL DEFAULT 1
);
-- One account can belong to one student, and a student can link one account per provider.
CREATE UNIQUE INDEX ux_logins_subject ON student_logins(provider, provider_subject);
CREATE UNIQUE INDEX ux_logins_email   ON student_logins(provider, email);
CREATE UNIQUE INDEX ux_logins_student ON student_logins(student_id, provider);

CREATE TABLE audit_log (
    id           INTEGER PRIMARY KEY,
    occurred_utc TEXT NOT NULL,
    actor        TEXT NOT NULL,
    session_id   TEXT NOT NULL,
    action       TEXT NOT NULL,
    entity_type  TEXT,
    entity_id    INTEGER,
    old_value    TEXT,
    new_value    TEXT,
    detail       TEXT
);
CREATE INDEX ix_audit_when   ON audit_log(occurred_utc);
CREATE INDEX ix_audit_entity ON audit_log(entity_type, entity_id);

CREATE TABLE settings (
    key         TEXT PRIMARY KEY,
    value       TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by  TEXT NOT NULL
);

CREATE TABLE schema_version (
    version     INTEGER NOT NULL,
    applied_utc TEXT    NOT NULL,
    app_version TEXT    NOT NULL
);

-- Append-only, enforced here rather than trusted to the application.
CREATE TRIGGER trg_tx_no_update BEFORE UPDATE ON transactions
BEGIN SELECT RAISE(ABORT, 'transactions are append-only'); END;

CREATE TRIGGER trg_tx_no_delete BEFORE DELETE ON transactions
BEGIN SELECT RAISE(ABORT, 'transactions cannot be deleted'); END;

CREATE TRIGGER trg_lines_no_update BEFORE UPDATE ON transaction_lines
BEGIN SELECT RAISE(ABORT, 'transaction lines are append-only'); END;

CREATE TRIGGER trg_lines_no_delete BEFORE DELETE ON transaction_lines
BEGIN SELECT RAISE(ABORT, 'transaction lines cannot be deleted'); END;

CREATE TRIGGER trg_audit_no_update BEFORE UPDATE ON audit_log
BEGIN SELECT RAISE(ABORT, 'the audit log is append-only'); END;

CREATE TRIGGER trg_audit_no_delete BEFORE DELETE ON audit_log
BEGIN SELECT RAISE(ABORT, 'the audit log is append-only'); END;

-- Café-wide defaults. The floor is the one the café asked for.
INSERT INTO settings (key, value, updated_utc, updated_by) VALUES
    ('cafe_name',                  'Skolans Café',   '1970-01-01T00:00:00Z', 'system'),
    ('minimum_balance_ore',        '-1000',          '1970-01-01T00:00:00Z', 'system'),
    ('max_lines_per_purchase',     '10',             '1970-01-01T00:00:00Z', 'system'),
    ('undo_window_minutes',        '15',             '1970-01-01T00:00:00Z', 'system'),
    ('large_purchase_warning_ore', '50000',          '1970-01-01T00:00:00Z', 'system'),
    ('low_balance_warning_ore',    '2000',           '1970-01-01T00:00:00Z', 'system'),
    ('busyness',                   '0',              '1970-01-01T00:00:00Z', 'system'),
    ('student_site_enabled',       '0',              '1970-01-01T00:00:00Z', 'system'),
    ('student_site_show_history',  '1',              '1970-01-01T00:00:00Z', 'system'),
    ('student_site_show_busyness', '1',              '1970-01-01T00:00:00Z', 'system'),
    ('student_site_history_days',  '90',             '1970-01-01T00:00:00Z', 'system');
