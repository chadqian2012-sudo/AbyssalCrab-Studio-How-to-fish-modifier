using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HowToFishTrainer.App;

internal static class AtomicLocalFile
{
    internal static T ReadVersioned<T>(string text, int latestVersion) where T : class
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("文件根节点无效");
        // A newer format may have changed even the types of previously known fields.
        if (root.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.Number &&
            (!version.TryGetInt32(out var number) || number < 1 || number > latestVersion))
            throw new NotSupportedException("文件版本不受当前软件支持");
        return root.Deserialize<T>() ?? throw new JsonException("文件为空");
    }

    internal static void Write(string path, string text, Action<string> validate)
        => WriteBytes(path, Encoding.UTF8.GetBytes(text), file => validate(File.ReadAllText(file)));

    internal static void WriteBytes(string path, byte[] bytes, Action<string>? validatePreviousFile = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (!File.Exists(path)) File.Move(temp, path);
            else
            {
                string? backup = null;
                if (validatePreviousFile != null)
                {
                    try { validatePreviousFile(path); backup = path + ".bak"; }
                    catch (NotSupportedException e) { throw new IOException("文件版本较新，未覆盖原文件", e); }
                    catch (Exception e) when (e is JsonException or InvalidOperationException or InvalidDataException) { }
                    // I/O errors must abort; only known invalid data may be replaced without rotating the backup.
                }
                File.Replace(temp, path, backup);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
