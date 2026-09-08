using CashCafe.Domain;

namespace CashCafe.Data;

/// <summary>
/// The shapes Dapper reads straight out of SQLite, and the mapping to domain types.
///
/// The mapping is written by hand rather than configured, so that the one conversion that
/// matters — an integer column of öre becoming a <see cref="Money"/> — is visible in one
/// place and cannot be silently widened to a floating point type by a convention.
/// </summary>
internal static class Rows
{
    public sealed class StudentRow
    {
        public long id { get; set; }
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string display_name { get; set; } = string.Empty;
        public string search_name { get; set; } = string.Empty;
        public string? class_name { get; set; }
        public long balance_ore { get; set; }
        public long? credit_limit_ore { get; set; }
        public long is_active { get; set; }
        public string? note { get; set; }
        public long? merged_into_id { get; set; }
        public string created_utc { get; set; } = string.Empty;
        public string created_by { get; set; } = string.Empty;
        public string? last_activity_utc { get; set; }
        public long has_login { get; set; }

        public Student ToDomain() => new()
        {
            Id = id,
            FirstName = first_name,
            LastName = last_name,
            DisplayName = display_name,
            SearchName = search_name,
            ClassName = class_name,
            Balance = new Money(balance_ore),
            CreditLimit = credit_limit_ore is null ? null : new Money(credit_limit_ore.Value),
            IsActive = is_active != 0,
            Note = note,
            MergedIntoId = merged_into_id,
            CreatedUtc = ParseTime(created_utc),
            CreatedBy = created_by,
            LastActivityUtc = last_activity_utc is null ? null : ParseTime(last_activity_utc),
            HasLogin = has_login != 0,
        };
    }

    public sealed class ItemRow
    {
        public long id { get; set; }
        public string name { get; set; } = string.Empty;
        public string search_name { get; set; } = string.Empty;
        public string? category { get; set; }
        public long price_ore { get; set; }
        public string? shortcut_key { get; set; }
        public long is_available { get; set; }
        public long is_archived { get; set; }
        public long sort_order { get; set; }
        public string created_utc { get; set; } = string.Empty;

        public Item ToDomain() => new()
        {
            Id = id,
            Name = name,
            SearchName = search_name,
            Category = category,
            Price = new Money(price_ore),
            ShortcutKey = shortcut_key,
            IsAvailable = is_available != 0,
            IsArchived = is_archived != 0,
            SortOrder = (int)sort_order,
            CreatedUtc = ParseTime(created_utc),
        };
    }

    public sealed class TransactionRow
    {
        public long id { get; set; }
        public long student_id { get; set; }
        public string type { get; set; } = string.Empty;
        public long amount_ore { get; set; }
        public long balance_after_ore { get; set; }
        public string occurred_utc { get; set; } = string.Empty;
        public string recorded_utc { get; set; } = string.Empty;
        public string @operator { get; set; } = string.Empty;
        public string session_id { get; set; } = string.Empty;
        public string? method { get; set; }
        public string? reference { get; set; }
        public string? reason { get; set; }
        public long? reverses_id { get; set; }
        public long? import_id { get; set; }
        public string? note { get; set; }

        public Transaction ToDomain(IReadOnlyList<TransactionLine>? lines = null) => new()
        {
            Id = id,
            StudentId = student_id,
            Type = ParseType(type),
            Amount = new Money(amount_ore),
            BalanceAfter = new Money(balance_after_ore),
            OccurredUtc = ParseTime(occurred_utc),
            RecordedUtc = ParseTime(recorded_utc),
            Operator = @operator,
            SessionId = session_id,
            Method = method is null ? null : Enum.Parse<DepositMethod>(method),
            Reference = reference,
            Reason = reason,
            ReversesId = reverses_id,
            ImportId = import_id,
            Note = note,
            Lines = lines ?? Array.Empty<TransactionLine>(),
        };
    }

    public sealed class LineRow
    {
        public long id { get; set; }
        public long transaction_id { get; set; }
        public long line_no { get; set; }
        public long? item_id { get; set; }
        public string item_name { get; set; } = string.Empty;
        public long unit_price_ore { get; set; }
        public long quantity { get; set; }
        public long line_total_ore { get; set; }
        public long is_custom { get; set; }

        public TransactionLine ToDomain() => new()
        {
            Id = id,
            TransactionId = transaction_id,
            LineNo = (int)line_no,
            ItemId = item_id,
            ItemName = item_name,
            UnitPrice = new Money(unit_price_ore),
            Quantity = (int)quantity,
            LineTotal = new Money(line_total_ore),
            IsCustom = is_custom != 0,
        };
    }

    public sealed class LoginRow
    {
        public long id { get; set; }
        public long student_id { get; set; }
        public string provider { get; set; } = string.Empty;
        public string? provider_subject { get; set; }
        public string email { get; set; } = string.Empty;
        public string linked_utc { get; set; } = string.Empty;
        public string linked_by { get; set; } = string.Empty;
        public string? claimed_utc { get; set; }
        public string? last_seen_utc { get; set; }
        public long is_enabled { get; set; }

        public StudentLogin ToDomain() => new()
        {
            Id = id,
            StudentId = student_id,
            Provider = provider,
            ProviderSubject = provider_subject,
            ClaimedUtc = claimed_utc is null ? null : ParseTime(claimed_utc),
            Email = email,
            LinkedUtc = ParseTime(linked_utc),
            LinkedBy = linked_by,
            LastSeenUtc = last_seen_utc is null ? null : ParseTime(last_seen_utc),
            IsEnabled = is_enabled != 0,
        };
    }

    public static string Format(DateTimeOffset when) => when.ToUniversalTime().ToString("O");

    public static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, Money.Culture, System.Globalization.DateTimeStyles.RoundtripKind);

    public static string TypeName(TransactionType type) => type switch
    {
        TransactionType.Purchase => "PURCHASE",
        TransactionType.Deposit => "DEPOSIT",
        TransactionType.Adjustment => "ADJUSTMENT",
        TransactionType.Reversal => "REVERSAL",
        TransactionType.Import => "IMPORT",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static TransactionType ParseType(string value) => value switch
    {
        "PURCHASE" => TransactionType.Purchase,
        "DEPOSIT" => TransactionType.Deposit,
        "ADJUSTMENT" => TransactionType.Adjustment,
        "REVERSAL" => TransactionType.Reversal,
        "IMPORT" => TransactionType.Import,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown transaction type '{value}'."),
    };

    /// <summary>
    /// The student columns plus whether an account is linked, which the website needs and
    /// the admin student list shows. Kept in one place so every read stays consistent.
    /// </summary>
    public const string StudentSelect = """
        SELECT s.*, EXISTS (SELECT 1 FROM student_logins l
                            WHERE l.student_id = s.id AND l.is_enabled = 1) AS has_login
        FROM students s
        """;

    public sealed class AuditRow
    {
        public long id { get; set; }
        public string occurred_utc { get; set; } = string.Empty;
        public string actor { get; set; } = string.Empty;
        public string session_id { get; set; } = string.Empty;
        public string action { get; set; } = string.Empty;
        public string? entity_type { get; set; }
        public long? entity_id { get; set; }
        public string? old_value { get; set; }
        public string? new_value { get; set; }
        public string? detail { get; set; }

        public AuditEntry ToDomain() => new()
        {
            Id = id,
            OccurredUtc = Rows.ParseTime(occurred_utc),
            Actor = actor,
            SessionId = session_id,
            Action = action,
            EntityType = entity_type,
            EntityId = entity_id,
            OldValue = old_value,
            NewValue = new_value,
            Detail = detail,
        };
    }

    public sealed class PriceRow
    {
        public long item_id { get; set; }
        public string item_name { get; set; } = string.Empty;
        public long price_ore { get; set; }
        public string valid_from_utc { get; set; } = string.Empty;
        public string? valid_to_utc { get; set; }
        public string changed_by { get; set; } = string.Empty;
        public string? reason { get; set; }

        public PriceHistoryEntry ToDomain() => new()
        {
            ItemId = item_id,
            ItemName = item_name,
            Price = new Money(price_ore),
            ValidFromUtc = Rows.ParseTime(valid_from_utc),
            ValidToUtc = valid_to_utc is null ? null : Rows.ParseTime(valid_to_utc),
            ChangedBy = changed_by,
            Reason = reason,
        };
    }
}
