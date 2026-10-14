using System.Text.Json;

namespace ClipNest.Core.Environment;

/// <summary>Small bootstrap file stored outside the movable data directory.</summary>
public sealed record DataLocationSettings(string DataRoot)
{
    public static DataLocationSettings? Load(string file)
        => File.Exists(file) ? JsonSerializer.Deserialize<DataLocationSettings>(File.ReadAllText(file)) : null;

    public void Save(string file)
    {
        if (!Path.IsPathFullyQualified(DataRoot)) throw new ArgumentException("数据目录必须是完整路径。");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, file, true);
    }
}
