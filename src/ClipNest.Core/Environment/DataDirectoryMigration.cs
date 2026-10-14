using ClipNest.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace ClipNest.Core.Environment;

/// <summary>Copies a quiescent data set; never removes the source or overwrites target files.</summary>
public static class DataDirectoryMigration
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
            throw new ArgumentException("请输入完整的数据文件夹路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }

    public static bool SameLocation(string first, string second)
        => string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    public static void ValidateTarget(string source, string destination)
    {
        source = Normalize(source);
        destination = Normalize(destination);
        if (SameLocation(destination, Path.GetPathRoot(destination)!))
            throw new IOException("请选择专门存放拾贴数据的文件夹，不能直接使用磁盘根目录。");
        if (SameLocation(source, destination)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("新旧数据文件夹不能相同，也不能互相包含。");
        RejectLinkedAncestors(source);
        RejectLinkedAncestors(destination);
        if (File.Exists(destination) || (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()))
            throw new IOException("目标文件夹不是空的。请选择空文件夹，以免覆盖已有数据。");
    }

    private static void RejectLinkedAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("请选择普通文件夹，不支持链接或目录联接。");
    }

    public static AppPaths Copy(IAppPaths source, string destination, ClipNestDatabase database)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(database);
        ValidateTarget(source.Root, destination);
        var target = AppPaths.FromRoot(Normalize(destination));
        Directory.CreateDirectory(target.Root);
        // A failed copy remains clearly marked; the application keeps using the source.
        var marker = Path.Combine(target.Root, "migration-incomplete.txt");
        File.WriteAllText(marker, "Incomplete ClipNest migration. The original data directory is unchanged.");
        Directory.CreateDirectory(target.Database);
        using (var original = database.Open())
        using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(target.Database, "clipnest.db"), Pooling = false }.ToString()))
        {
            backup.Open();
            original.BackupDatabase(backup);
            using var check = backup.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                throw new IOException("迁移后的数据库校验失败，已保留原数据。");
        }
        CopyFolder(source.Blobs, target.Blobs);
        foreach (var file in new[] { "settings.json", "appearance.json" })
        {
            var input = Path.Combine(source.Root, file);
            if (File.Exists(input)) File.Copy(input, Path.Combine(target.Root, file), false);
        }
        File.Delete(marker);
        return target;
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        if (!Directory.Exists(source)) return;
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("数据中包含链接文件，请先移除链接后再迁移。");
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo) CopyFolder(entry.FullName, target);
            else File.Copy(entry.FullName, target, false);
        }
    }
}
