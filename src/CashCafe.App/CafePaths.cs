using System;
using System.IO;
using System.Linq;

namespace CashCafe.App;

/// <summary>
/// Where everything lives on disk.
///
/// The database goes in ProgramData rather than a user profile on purpose: café staff sign
/// in to whichever Windows account is nearest, and a balance that depended on who was logged
/// in would be a disaster. Portable mode keeps everything next to the exe so a whole café
/// fits on a USB stick.
/// </summary>
public static class CafePaths
{
    public static bool IsPortable { get; private set; }

    public static string Root { get; private set; } = DefaultRoot();

    public static string Database => Path.Combine(Root, "cashcafe.db");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Backups => Path.Combine(Root, "Backups");
    public static string Imports => Path.Combine(Root, "Imports");
    public static string Logs => Path.Combine(Root, "Logs");

    public static void UsePortableMode()
    {
        IsPortable = true;
        Root = Path.Combine(AppContext.BaseDirectory, "data");
    }

    public static void EnsureFolders()
    {
        foreach (var folder in new[] { Root, Backups, Imports, Logs })
            Directory.CreateDirectory(folder);
    }

    /// <summary>Keeps a copy of every imported file, so a number can be traced to its source.</summary>
    public static string ArchiveImport(string sourcePath)
    {
        Directory.CreateDirectory(Imports);

        var stamped = $"{DateTimeOffset.Now:yyyy-MM-ddTHH-mm-ss}_{Path.GetFileName(sourcePath)}";
        var destination = Path.Combine(Imports, stamped);
        File.Copy(sourcePath, destination, overwrite: false);

        return destination;
    }

    private static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CashCafe");
}
