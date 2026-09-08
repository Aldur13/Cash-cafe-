using CashCafe.Domain;
using Dapper;

namespace CashCafe.Data;

public enum SignInOutcome
{
    /// <summary>The account is linked to a student and may see their balance.</summary>
    Recognised,

    /// <summary>The address was registered by the café and has now been bound to this account.</summary>
    Claimed,

    /// <summary>Nobody at the café registered this address. The café must add it first.</summary>
    NotRegistered,

    /// <summary>The link exists but an admin has switched it off.</summary>
    Disabled,

    /// <summary>The address was registered but a different account already claimed it.</summary>
    AlreadyClaimedByAnotherAccount,

    /// <summary>The student behind the link has been deactivated.</summary>
    StudentInactive,
}

public sealed record SignInResult(SignInOutcome Outcome, Student? Student, string Message)
{
    public bool IsSignedIn => Outcome is SignInOutcome.Recognised or SignInOutcome.Claimed;
}

/// <summary>
/// Links between school accounts and students, used only by the website.
///
/// The café never stores a password, a password hash, or anything that could be used to
/// sign in anywhere: authentication happens entirely at Google or Microsoft, and all this
/// table holds is "this school address belongs to this student".
/// </summary>
public sealed class StudentLoginRepository(CafeDatabase database)
{
    /// <summary>
    /// Registers a school address so that person can sign in to the site. Called by an admin,
    /// or by the bulk import of a class list. The link is not usable until the owner of the
    /// address actually signs in, which is what binds it to one specific account.
    /// </summary>
    public long Register(long studentId, string provider, string email, string actor)
    {
        var normalized = NormalizeEmail(email);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var id = connection.ExecuteScalar<long>(
            """
            INSERT INTO student_logins (student_id, provider, provider_subject, email,
                                        linked_utc, linked_by, is_enabled)
            VALUES ($student, $provider, NULL, $email, $now, $actor, 1);
            SELECT last_insert_rowid();
            """,
            new
            {
                student = studentId,
                provider = provider.ToLowerInvariant(),
                email = normalized,
                now = Rows.Format(DateTimeOffset.UtcNow),
                actor,
            }, transaction);

        Audit.Write(connection, transaction, actor, "admin", "LOGIN_REGISTER", "student", studentId,
            null, normalized, provider);

        transaction.Commit();
        return id;
    }

    /// <summary>
    /// Resolves a completed sign-in at Google or Microsoft to a student.
    ///
    /// Matching is by the provider's stable subject id first, so a school that later renames
    /// an address does not lock the student out. An address that has been registered but never
    /// used is claimed here, once, and bound to the account that used it.
    /// </summary>
    public SignInResult SignIn(string provider, string subject, string email, string? sessionId = null)
    {
        var normalizedProvider = provider.ToLowerInvariant();
        var normalizedEmail = NormalizeEmail(email);
        var now = Rows.Format(DateTimeOffset.UtcNow);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var link = connection.QuerySingleOrDefault<Rows.LoginRow>(
            """
            SELECT * FROM student_logins
            WHERE provider = $provider AND provider_subject = $subject
            """,
            new { provider = normalizedProvider, subject }, transaction)?.ToDomain();

        var outcome = SignInOutcome.Recognised;

        if (link is null)
        {
            // No account bound yet — is this address registered and waiting to be claimed?
            var pending = connection.QuerySingleOrDefault<Rows.LoginRow>(
                """
                SELECT * FROM student_logins
                WHERE provider = $provider AND email = $email
                """,
                new { provider = normalizedProvider, email = normalizedEmail }, transaction)?.ToDomain();

            if (pending is null)
                return new SignInResult(SignInOutcome.NotRegistered, null,
                    "No café account is connected to this address. Ask in the café and they can add it.");

            if (pending.IsClaimed)
                return new SignInResult(SignInOutcome.AlreadyClaimedByAnotherAccount, null,
                    "This address is already connected to a different sign-in. Ask in the café.");

            if (!pending.IsEnabled)
                return new SignInResult(SignInOutcome.Disabled, null,
                    "This account has been switched off. Ask in the café.");

            connection.Execute(
                """
                UPDATE student_logins
                SET provider_subject = $subject, claimed_utc = $now, last_seen_utc = $now
                WHERE id = $id AND provider_subject IS NULL
                """,
                new { id = pending.Id, subject, now }, transaction);

            Audit.Write(connection, transaction, "site", sessionId ?? "site", "LOGIN_CLAIM", "student",
                pending.StudentId, null, normalizedEmail, normalizedProvider);

            link = pending;
            outcome = SignInOutcome.Claimed;
        }
        else
        {
            if (!link.IsEnabled)
                return new SignInResult(SignInOutcome.Disabled, null,
                    "This account has been switched off. Ask in the café.");

            connection.Execute("UPDATE student_logins SET last_seen_utc = $now WHERE id = $id",
                new { id = link.Id, now }, transaction);
        }

        var student = connection.QuerySingleOrDefault<Rows.StudentRow>(
            Rows.StudentSelect + " WHERE s.id = $id", new { id = link.StudentId }, transaction)?.ToDomain();

        if (student is null || !student.IsActive)
            return new SignInResult(SignInOutcome.StudentInactive, null,
                "This café account is not active any more. Ask in the café.");

        transaction.Commit();

        return new SignInResult(outcome, student, $"Signed in as {student.DisplayName}");
    }

    public IReadOnlyList<StudentLogin> ForStudent(long studentId)
    {
        using var connection = database.Open();
        return connection.Query<Rows.LoginRow>(
                "SELECT * FROM student_logins WHERE student_id = $id ORDER BY provider",
                new { id = studentId })
            .Select(r => r.ToDomain()).ToList();
    }

    public IReadOnlyList<StudentLogin> All()
    {
        using var connection = database.Open();
        return connection.Query<Rows.LoginRow>("SELECT * FROM student_logins ORDER BY email")
            .Select(r => r.ToDomain()).ToList();
    }

    public void SetEnabled(long loginId, bool enabled, string actor)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute("UPDATE student_logins SET is_enabled = $enabled WHERE id = $id",
            new { id = loginId, enabled = enabled ? 1 : 0 }, transaction);

        Audit.Write(connection, transaction, actor, "admin",
            enabled ? "LOGIN_ENABLE" : "LOGIN_DISABLE", "login", loginId, null, null, null);

        transaction.Commit();
    }

    /// <summary>
    /// Removes a link entirely — the student can no longer sign in, and the address becomes
    /// free to register again. Used when a student leaves, or when a link was made by mistake.
    /// </summary>
    public void Remove(long loginId, string actor)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var email = connection.QuerySingleOrDefault<string?>(
            "SELECT email FROM student_logins WHERE id = $id", new { id = loginId }, transaction);

        connection.Execute("DELETE FROM student_logins WHERE id = $id", new { id = loginId }, transaction);

        Audit.Write(connection, transaction, actor, "admin", "LOGIN_REMOVE", "login", loginId,
            email, null, null);

        transaction.Commit();
    }

    /// <summary>
    /// Sign-in addresses are compared lower-cased and trimmed. Nothing clever beyond that:
    /// treating "a.b@x" and "ab@x" as the same address is a provider-specific assumption and
    /// getting it wrong would show one student another student's balance.
    /// </summary>
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
