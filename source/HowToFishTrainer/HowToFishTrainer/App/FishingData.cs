using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HowToFishTrainer.App;

public sealed record FishingSpecies(int Id, string Name);
public sealed record FishingCatch(Guid Id, Guid SessionId, DateTimeOffset At, string Species, int Count,
    double? WeightKg, string Place, string Equipment, string Note)
{
    public string Display => $"{At.LocalDateTime:MM-dd HH:mm}  ·  {Species} ×{Count}  ·  {(WeightKg is double w ? w.ToString("0.###") + " kg/条" : "重量未记录")}\n{Place}  {Equipment}";
}
public sealed record FishingSession(Guid Id, string Name, DateTimeOffset Started, DateTimeOffset LastActive,
    DateTimeOffset? Ended = null, string EndReason = "")
{
    public string Display => $"{Started.LocalDateTime:MM-dd HH:mm} · {Name} · {(Ended == null ? "进行中" : EndReason)}";
}
public sealed record FishingCodexRow(int Id, string Name, bool Discovered, int LoggedCount, double? BestKg)
{
    public string Display => $"{Name}  ·  {(Discovered ? "存档已发现" : "存档未发现")}  ·  日志 {LoggedCount} 条  ·  {(BestKg is double w ? $"个人最大 {w:0.###} kg" : "暂无重量纪录")}";
}
public sealed class FishingData
{
    public int Version { get; set; } = 1;
    public List<FishingCatch> Catches { get; set; } = new();
    public List<FishingSession> Sessions { get; set; } = new();
    public string SaveRoot { get; set; } = "";
    public string Account { get; set; } = "";
    public bool AutoBackup { get; set; }
    public string LastSpecies { get; set; } = "";
    public int LastCount { get; set; } = 1;
    public double? LastWeightKg { get; set; }
    public string LastPlace { get; set; } = "";
    public string LastEquipment { get; set; } = "";
    public string LastNote { get; set; } = "";
    public string LastSessionName { get; set; } = "休闲钓鱼";

    // Catch/session records contain only immutable values. Copy the mutable lists;
    // persistence still validates the complete proposed state before it is published.
    internal FishingData Copy() => new()
    {
        Version = Version, Catches = new(Catches), Sessions = new(Sessions),
        SaveRoot = SaveRoot, Account = Account, AutoBackup = AutoBackup,
        LastSpecies = LastSpecies, LastCount = LastCount, LastWeightKg = LastWeightKg,
        LastPlace = LastPlace, LastEquipment = LastEquipment, LastNote = LastNote,
        LastSessionName = LastSessionName
    };
}

// This module contains only local files and user-entered records. No game/runtime dependency.
public sealed class FishingDataStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string? _path;
    private FileStream? _lease;
    private const int MaxFileBytes = 16 * 1024 * 1024;
    public bool Writable { get; private set; } = true;
    public string Warning { get; private set; } = "";
    public FishingDataStore(string? path)
    {
        _path = path;
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        { Writable = false; Warning = "日志被其他窗口占用或目录不可写；当前仅可查看和导出"; }
    }
    public static FishingData Parse(string json)
    {
        var d = AtomicLocalFile.ReadVersioned<FishingData>(json, 1);
        if (d.Catches == null || d.Sessions == null || d.Catches.Count > 20000 || d.Sessions.Count > 1000)
            throw new InvalidDataException("日志条目数量无效");
        var sessions = new HashSet<Guid>(); var catches = new HashSet<Guid>();
        foreach (var s in d.Sessions)
            if (s == null || s.Id == Guid.Empty || !sessions.Add(s.Id) || !Text(s.Name, 80) || s.Started.Year < 2000
                || s.LastActive < s.Started || (s.Ended != null && (s.Ended < s.Started || s.Ended < s.LastActive)) || !Text(s.EndReason, 80, true))
                throw new InvalidDataException("场次记录无效");
        if (d.Sessions.Count(s => s.Ended == null) > 1) throw new InvalidDataException("同时存在多个进行中场次");
        foreach (var c in d.Catches)
            if (c == null || c.Id == Guid.Empty || !catches.Add(c.Id) || !sessions.Contains(c.SessionId) || !Text(c.Species, 80)
                || c.Count < 1 || c.Count > 10000 || c.At.Year < 2000 || (c.WeightKg is double w && (!double.IsFinite(w) || w <= 0 || w > 1000000))
                || !Text(c.Place, 120, true) || !Text(c.Equipment, 120, true) || !Text(c.Note, 500, true))
                throw new InvalidDataException("鱼获记录无效");
        d.SaveRoot ??= ""; d.Account ??= "";
        if (!Text(d.LastSpecies, 80, true)) d.LastSpecies = "";
        if (d.LastCount < 1 || d.LastCount > 10000) d.LastCount = 1;
        if (d.LastWeightKg is double last && (!double.IsFinite(last) || last <= 0 || last > 1000000)) d.LastWeightKg = null;
        if (!Text(d.LastPlace, 120, true)) d.LastPlace = "";
        if (!Text(d.LastEquipment, 120, true)) d.LastEquipment = "";
        if (!Text(d.LastNote, 500, true)) d.LastNote = "";
        if (!Text(d.LastSessionName, 80)) d.LastSessionName = "休闲钓鱼";
        return d;
    }
    private static bool Text(string? s, int max, bool empty = false) => s != null && s.Length <= max && (empty || !string.IsNullOrWhiteSpace(s));
    public FishingData Load()
    {
        if (_path == null) return new();
        foreach (var p in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(p)) continue;
            try
            {
                var d = ReadFile(p);
                if (p != _path) Warning = "日志主文件损坏，已读取上一份有效备份";
                return d;
            }
            catch (NotSupportedException) { Writable = false; Warning = "日志版本较新，原文件保持不变"; return new(); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { Warning = "日志读取失败，正在尝试有效备份"; }
        }
        return new();
    }
    public void Save(FishingData data)
    {
        if (!Writable) throw new IOException(Warning);
        var text = JsonSerializer.Serialize(data, JsonOptions); Parse(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxFileBytes) throw new IOException("日志已达 16 MB 上限，请先导出整理");
        if (_path != null) AtomicLocalFile.WriteBytes(_path, bytes, file => ReadFile(file));
    }
    private static FishingData ReadFile(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new IOException("日志超过 16 MB");
        return Parse(File.ReadAllText(path));
    }
    public void Dispose() { _lease?.Dispose(); _lease = null; Writable = false; }
}
