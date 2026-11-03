using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HowToFishTrainer.App;

public sealed record SaveBackupFile(string RelativePath, long Length, string Sha256);
public sealed class SaveBackupManifest
{
    public int Version { get; set; } = 1;
    public DateTimeOffset Created { get; set; }
    public string SourceRoot { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<SaveBackupFile> Files { get; set; } = new();
}
public sealed record SaveBackupEntry(string Directory, SaveBackupManifest Manifest)
{
    internal string? SourceSignature { get; init; }
    public string Display => $"{Manifest.Created.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {Manifest.Reason} · {Manifest.Files.Count} 个文件 · {Manifest.Files.Sum(f => f.Length) / 1024d:0.0} KB";
}

public sealed class SaveBackupService
{
    public static string DefaultSaveRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Dazed Games", "How to Fish", "Saves");
    private readonly string _backups;
    private readonly Func<bool> _gameRunning;
    private readonly object _gate = new();
    public SaveBackupService(string backups, Func<bool>? gameRunning = null)
    { _backups = Root(backups); _gameRunning = gameRunning ?? IsGameRunning; }
    public static bool IsGameRunning()
    {
        try { var processes = Process.GetProcessesByName("How to Fish"); try { return processes.Length != 0; } finally { foreach (var p in processes) p.Dispose(); } }
        catch { return true; } // A failed process check cannot authorize overwriting saves.
    }
    internal static string Root(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Under(string path, string root) => Path.GetFullPath(path).StartsWith(Root(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static void NoLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current != null)
        {
            if ((File.Exists(current) || System.IO.Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不支持链接、联接或重解析目录：" + current);
            current = Path.GetDirectoryName(current);
        }
    }
    private static bool SaveName(string name) => name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".txt.backup", StringComparison.OrdinalIgnoreCase);
    private static bool RelativeSave(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Length is 1 or 2 && parts.All(p => p.Length > 0 && p != "." && p != ".." && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            && (parts.Length == 1 || parts[0].All(char.IsAsciiDigit)) && SaveName(parts[^1]);
    }
    private List<string> SaveFiles(string saveRoot, bool allowEmpty = false)
    {
        string root = Root(saveRoot); NoLinks(root); NoLinks(_backups);
        if (root.Equals(Root(_backups), StringComparison.OrdinalIgnoreCase) || Under(_backups, root) || Under(root, _backups))
            throw new IOException("备份目录与游戏存档目录不能重叠");
        if (!System.IO.Directory.Exists(root)) { if (allowEmpty) return new(); throw new DirectoryNotFoundException("未找到存档目录"); }
        var files = System.IO.Directory.GetFiles(root).Where(f => SaveName(Path.GetFileName(f))).ToList();
        foreach (var dir in System.IO.Directory.GetDirectories(root).Where(d => Path.GetFileName(d).All(char.IsAsciiDigit)))
        { NoLinks(dir); files.AddRange(System.IO.Directory.GetFiles(dir).Where(f => SaveName(Path.GetFileName(f)))); }
        if (files.Count > 1000 || (files.Count == 0 && !allowEmpty)) throw new InvalidDataException("存档文件数量需要在 1–1000 之间");
        foreach (var f in files) { NoLinks(f); if (new FileInfo(f).Length > 64 * 1024 * 1024) throw new IOException("单个存档超过 64 MB"); }
        if (files.Sum(f => new FileInfo(f).Length) > 256L * 1024 * 1024) throw new IOException("存档总量超过 256 MB");
        return files.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] ReadNonBlocking(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        var buffer = new byte[81920]; int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (memory.Length + count > 64 * 1024 * 1024) throw new IOException("存档在读取过程中超出上限");
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }
    private Dictionary<string, byte[]> Snapshot(string saveRoot, bool allowEmpty = false)
        => Snapshot(saveRoot, allowEmpty, out _);
    private Dictionary<string, byte[]> Snapshot(string saveRoot, bool allowEmpty, out string sourceSignature)
    {
        var files = SaveFiles(saveRoot, allowEmpty);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        var stamps = files.ToDictionary(f => f, f => (Length: new FileInfo(f).Length, Written: File.GetLastWriteTimeUtc(f)));
        foreach (var path in files)
        {
            byte[] bytes = ReadNonBlocking(path); totalBytes += bytes.LongLength;
            if (totalBytes > 256L * 1024 * 1024) throw new IOException("存档在读取过程中总量超出 256 MB 上限");
            result.Add(Path.GetRelativePath(Root(saveRoot), path), bytes);
        }
        if (!files.SequenceEqual(SaveFiles(saveRoot, allowEmpty), StringComparer.OrdinalIgnoreCase)
            || stamps.Any(p => (new FileInfo(p.Key).Length, File.GetLastWriteTimeUtc(p.Key)) != p.Value))
            throw new IOException("存档正在写入，请稍后重试备份");
        // Sharing must not block the game's atomic save replacement. A second content pass
        // plus metadata/file-set checks rejects a changing snapshot without locking the game.
        foreach (var path in files)
            if (Hash(ReadNonBlocking(path)) != Hash(result[Path.GetRelativePath(Root(saveRoot), path)]))
                throw new IOException("存档内容正在更改，请稍后重试备份");
        if (!files.SequenceEqual(SaveFiles(saveRoot, allowEmpty), StringComparer.OrdinalIgnoreCase)
            || stamps.Any(p => (new FileInfo(p.Key).Length, File.GetLastWriteTimeUtc(p.Key)) != p.Value))
            throw new IOException("存档在复核过程中发生变化，请稍后重试备份");
        string root = Root(saveRoot);
        sourceSignature = string.Join("|", files.Select(f => $"{Path.GetRelativePath(root, f)}:{stamps[f].Length}:{stamps[f].Written.Ticks}"));
        return result;
    }
    private SaveBackupEntry Persist(string root, Dictionary<string, byte[]> files, string reason)
    {
        NoLinks(_backups); System.IO.Directory.CreateDirectory(_backups);
        if (System.IO.Directory.GetDirectories(_backups).Length >= 500) throw new IOException("已有 500 份备份，请先手动归档备份目录后继续");
        string id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string staging = Path.Combine(_backups, ".pending-" + id), final = Path.Combine(_backups, id);
        var manifest = new SaveBackupManifest { Created = DateTimeOffset.UtcNow, SourceRoot = Root(root), Reason = reason };
        System.IO.Directory.CreateDirectory(staging);
        try
        {
            foreach (var pair in files)
            {
                if (!RelativeSave(pair.Key)) throw new InvalidDataException("非法存档相对路径");
                string path = Path.Combine(staging, "data", pair.Key); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(pair.Value); f.Flush(true); }
                manifest.Files.Add(new(pair.Key, pair.Value.LongLength, Hash(pair.Value)));
            }
            FishingDataStore.WriteAtomic(Path.Combine(staging, "manifest.json"), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })));
            System.IO.Directory.Move(staging, final);
            return new(final, manifest);
        }
        finally { if (System.IO.Directory.Exists(staging) && Under(staging, _backups)) { NoLinks(staging); System.IO.Directory.Delete(staging, true); } }
    }
    public SaveBackupEntry Create(string root, string reason = "手动备份")
    { lock (_gate) { var snapshot = Snapshot(root, false, out string signature); return Persist(root, snapshot, reason) with { SourceSignature = signature }; } }
    public IReadOnlyList<SaveBackupEntry> List()
    {
        NoLinks(_backups); if (!System.IO.Directory.Exists(_backups)) return Array.Empty<SaveBackupEntry>();
        var list = new List<SaveBackupEntry>();
        foreach (string dir in System.IO.Directory.GetDirectories(_backups).Where(d => !Path.GetFileName(d).StartsWith('.')))
        { try { list.Add(ReadEntry(dir)); } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { } }
        return list.OrderByDescending(e => e.Manifest.Created).ToArray();
    }
    private SaveBackupEntry ReadEntry(string directory)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), _backups, StringComparison.OrdinalIgnoreCase)) throw new IOException("备份不属于当前备份目录");
        NoLinks(directory); string path = Path.Combine(directory, "manifest.json"); NoLinks(path);
        if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("备份清单过大");
        var m = JsonSerializer.Deserialize<SaveBackupManifest>(File.ReadAllText(path)) ?? throw new InvalidDataException("清单为空");
        if (m.Version != 1 || m.Files == null || m.Files.Count > 1000 || m.SourceRoot == null || (m.Files.Count == 0 && m.Reason != "恢复前自动备份")
            || m.Files.Any(f => f == null || f.RelativePath == null || !RelativeSave(f.RelativePath) || f.Length is < 0 or > 64 * 1024 * 1024 || f.Sha256 == null || f.Sha256.Length != 64)
            || m.Files.Select(f => f.RelativePath.Replace('/', '\\')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Files.Count
            || m.Files.Sum(f => f.Length) > 256L * 1024 * 1024) throw new InvalidDataException("备份清单无效");
        return new(directory, m);
    }
    public static void ValidateSave(string relative, byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        var r = json.RootElement;
        string name = Path.GetFileName(relative);
        bool local = name.Equals("local.txt", StringComparison.OrdinalIgnoreCase) || name.Equals("local.txt.backup", StringComparison.OrdinalIgnoreCase);
        if (r.ValueKind != JsonValueKind.Object || (local
            ? !r.TryGetProperty("CreaturesToList", out var a) || a.ValueKind != JsonValueKind.Array
            : !r.TryGetProperty("Name", out var n) || n.ValueKind != JsonValueKind.String || !r.TryGetProperty("Players", out var p) || p.ValueKind != JsonValueKind.Array || !r.TryGetProperty("WorldItems", out var w) || w.ValueKind != JsonValueKind.Array))
            throw new InvalidDataException("存档结构不符合当前游戏格式：" + relative);
    }
    public SaveBackupEntry Restore(SaveBackupEntry entry, string saveRoot)
    {
        lock (_gate)
        {
            if (_gameRunning()) throw new IOException("请完全退出 How to Fish 后再恢复存档");
            entry = ReadEntry(entry.Directory);
            if (entry.Manifest.Files.Count == 0) throw new IOException("此恢复前副本为空，没有可恢复的文件");
            string root = Root(saveRoot);
            if (!root.Equals(Root(entry.Manifest.SourceRoot), StringComparison.OrdinalIgnoreCase)) throw new IOException("当前存档目录与此备份的原始目录不同，已停止恢复");
            NoLinks(root); var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in entry.Manifest.Files)
            {
                string source = Path.Combine(entry.Directory, "data", file.RelativePath); NoLinks(source);
                if (!Under(source, entry.Directory) || new FileInfo(source).Length != file.Length) throw new InvalidDataException("备份文件缺失或长度不符");
                byte[] bytes = File.ReadAllBytes(source);
                if (!Hash(bytes).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份校验失败：" + file.RelativePath);
                ValidateSave(file.RelativePath, bytes);
                string target = Path.Combine(root, file.RelativePath); if (!Under(target, root)) throw new IOException("恢复路径越界"); NoLinks(target);
                payload.Add(file.RelativePath, bytes);
            }
            var prior = Snapshot(root, true);
            var recovery = Persist(root, prior, "恢复前自动备份"); // Must succeed before any save changes.
            var written = new List<string>();
            try
            {
                foreach (var pair in payload)
                {
                    if (_gameRunning()) throw new IOException("检测到游戏启动，已停止并回滚恢复");
                    string target = Path.Combine(root, pair.Key); NoLinks(target);
                    FishingDataStore.WriteAtomic(target, pair.Value); written.Add(pair.Key);
                }
                if (_gameRunning()) throw new IOException("检测到游戏启动，已停止并回滚恢复");
                return recovery;
            }
            catch (Exception original)
            {
                var rollbackErrors = new List<string>();
                foreach (var path in written.AsEnumerable().Reverse())
                {
                    try { string target = Path.Combine(root, path); NoLinks(target); if (prior.TryGetValue(path, out var bytes)) FishingDataStore.WriteAtomic(target, bytes); else File.Delete(target); }
                    catch (Exception e) { rollbackErrors.Add(e.Message); }
                }
                if (rollbackErrors.Count != 0) throw new IOException("恢复失败，部分回滚失败；恢复前完整副本：" + recovery.Directory + " · " + string.Join("；", rollbackErrors), original);
                throw new IOException("恢复未完成，已回滚原存档；恢复前副本：" + recovery.Directory + " · " + original.Message, original);
            }
        }
    }
    public string Signature(string root) => string.Join("|", SaveFiles(root).Select(f => $"{Path.GetRelativePath(root, f)}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f).Ticks}"));
}
