using System.Reflection;
using Microsoft.Data.Sqlite;

namespace CashCafe.Data;

/// <summary>
/// Opens connections to the café database and applies migrations.
///
/// Every connection is configured the same way: WAL for crash safety and concurrent
/// readers, FULL synchronous so a committed sale survives a power cut, foreign keys on,
/// and a busy timeout so a momentary lock waits rather than throwing at the counter.
/// </summary>
public sealed class CafeDatabase
{
    private readonly string _connectionString;

    public CafeDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>An in-memory database, used by the tests and by the demo data seeder.</summary>
    public static CafeDatabase InMemory(string name) =>
        new($"file:{name}?mode=memory&cache=shared");

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous  = FULL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        pragma.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// Applies every migration the database has not seen yet, in order, each inside its own
    /// transaction. Forward-only: to go back you restore the backup taken before the update.
    /// </summary>
    public int Migrate(string appVersion = "0.1.0")
    {
        using var connection = Open();
        var current = CurrentVersion(connection);
        var applied = 0;

        foreach (var (version, sql) in LoadMigrations().Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;
                stamp.CommandText =
                    "INSERT INTO schema_version (version, applied_utc, app_version) VALUES ($v, $t, $a)";
                stamp.Parameters.AddWithValue("$v", version);
                stamp.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                stamp.Parameters.AddWithValue("$a", appVersion);
                stamp.ExecuteNonQuery();
            }

            transaction.Commit();
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// The highest migration this database has had applied. Zero for a fresh file.
    /// A database from a newer version of the program is refused rather than written to.
    /// </summary>
    public static int CurrentVersion(SqliteConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='schema_version'";
        if (Convert.ToInt32(exists.ExecuteScalar()) == 0) return 0;

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT coalesce(max(version), 0) FROM schema_version";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int HighestKnownMigration() => LoadMigrations().Max(m => m.Version);

    private static IEnumerable<(int Version, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.Ordinal)))
        {
            // "CashCafe.Data.Migrations.001_initial.sql" → 1
            var fileName = name.Split('.')[^2];
            var digits = new string(fileName.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var version)) continue;

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (version, reader.ReadToEnd());
        }
    }
}
