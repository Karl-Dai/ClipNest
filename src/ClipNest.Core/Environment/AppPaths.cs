using System.IO;

namespace ClipNest.Core.Environment;

/// <summary>
/// Default <see cref="IAppPaths"/> implementation rooted at
/// <c>{baseDirectory}/ClipNest</c>. <see cref="Default"/> uses
/// <see cref="System.Environment.SpecialFolder.LocalApplicationData"/>.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private const string AppFolderName = "ClipNest";

    public AppPaths(string baseDirectory) : this(baseDirectory, false) { }

    private AppPaths(string baseDirectory, bool exactRoot)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new System.ArgumentException(
                "Base directory must be non-empty.", nameof(baseDirectory));
        }

        Root = exactRoot ? Path.GetFullPath(baseDirectory) : Path.Combine(baseDirectory, AppFolderName);
        Database = Path.Combine(Root, "db");
        Cache = Path.Combine(Root, "cache");
        Logs = Path.Combine(Root, "logs");
        Blobs = Path.Combine(Root, "blobs");
        SettingsFile = Path.Combine(Root, "settings.json");
    }

    public string Root { get; }
    public string Database { get; }
    public string Cache { get; }
    public string Logs { get; }
    public string Blobs { get; }
    public string SettingsFile { get; }

    public static AppPaths FromRoot(string root) => new(root, true);

    /// <summary>Default instance rooted at <c>%LOCALAPPDATA%\ClipNest</c>.</summary>
    public static AppPaths Default { get; } = new AppPaths(
        System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.LocalApplicationData));
}
