using CashCafe.Data;
using CashCafe.Domain;

namespace CashCafe.App.Core.Services;

/// <summary>
/// Everything a screen needs, in one place: the repositories, the current settings, and the
/// in-memory lists the till searches.
///
/// The lists are held in memory and refreshed explicitly rather than queried per keystroke,
/// which is what keeps the student search instant with a queue waiting.
/// </summary>
public sealed class CafeContext(
    StudentRepository students,
    ItemRepository items,
    LedgerRepository ledger,
    SettingsRepository settings,
    ReportRepository reports,
    AuditRepository audit)
{
    private List<Student> _students = new();
    private List<Item> _items = new();

    public StudentRepository Students { get; } = students;
    public ItemRepository Items { get; } = items;
    public LedgerRepository Ledger { get; } = ledger;
    public SettingsRepository SettingsRepository { get; } = settings;
    public ReportRepository Reports { get; } = reports;
    public AuditRepository Audit { get; } = audit;

    public CafeSettings Settings { get; private set; } = CafeSettings.Defaults;

    /// <summary>One id per run of the program. Every transaction records the session that made it.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];

    public string TillName { get; init; } = "Café (till 1)";

    public IReadOnlyList<Student> ActiveStudents => _students;
    public IReadOnlyList<Item> AvailableItems => _items;

    public void Refresh()
    {
        Settings = SettingsRepository.Load();
        _students = Students.All().ToList();
        _items = Items.All().ToList();
    }

    /// <summary>
    /// Updates one student in the in-memory list after a sale, so the till shows the new
    /// balance immediately without re-reading every student from the database.
    /// </summary>
    public void RefreshStudent(long studentId)
    {
        var updated = Students.Find(studentId);
        var index = _students.FindIndex(s => s.Id == studentId);

        if (updated is null || !updated.IsActive)
        {
            if (index >= 0) _students.RemoveAt(index);
            return;
        }

        if (index >= 0) _students[index] = updated;
        else _students.Add(updated);
    }
}
