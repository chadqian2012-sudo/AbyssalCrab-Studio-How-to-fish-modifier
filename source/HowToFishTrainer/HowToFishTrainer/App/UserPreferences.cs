using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HowToFishTrainer.App;

// Each leaf is read independently so a bad field cannot discard unrelated settings.
public sealed class ParameterValues : Dictionary<string, JsonElement>
{
    public ParameterValues() : base(StringComparer.Ordinal) { }
    public void Put<T>(string key, T value) => this[key] = JsonSerializer.SerializeToElement(value);
    public string? Text(string key) => TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    public bool? Boolean(string key) => TryGetValue(key, out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null;
    public double? Number(string key) => TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
    public ParameterValues Copy() { var result = new ParameterValues(); foreach (var pair in this) result[pair.Key] = pair.Value.Clone(); return result; }
    internal static ParameterValues Read(JsonElement e)
    {
        var result = new ParameterValues();
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject()) result[p.Name] = p.Value.Clone();
        return result;
    }
}

public sealed class TrainerUserPreferences
{
    public const int CurrentVersion = 1;
    public int SchemaVersion { get; } = CurrentVersion;
    public Dictionary<string, ParameterValues> Features { get; } = new(StringComparer.Ordinal);
    public ParameterValues General { get; private set; } = new();
    public ParameterValues Teleport { get; private set; } = new();
    public ParameterValues AimAssist { get; private set; } = new();
    public ParameterValues Crosshair { get; private set; } = new();
    public ParameterValues Roulette { get; private set; } = new();
    public ParameterValues SlotMachine { get; private set; } = new();
    public ParameterValues RoomMembers { get; private set; } = new();

    public static TrainerUserPreferences Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("参数文件根节点无效");
        if (root.TryGetProperty("schemaVersion", out var version) &&
            (!version.TryGetInt32(out var n) || n != CurrentVersion))
            throw new NotSupportedException("参数文件版本不受当前软件支持");
        ParameterValues Read(string key) => root.TryGetProperty(key, out var value) ? ParameterValues.Read(value) : new();
        var result = new TrainerUserPreferences {
            General = Read("general"), Teleport = Read("teleport"), AimAssist = Read("aimAssist"),
            Crosshair = Read("crosshair"), Roulette = Read("roulette"),
            SlotMachine = Read("slotMachine"), RoomMembers = Read("roomMembers")
        };
        if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Object)
            foreach (var feature in features.EnumerateObject())
                if (feature.Value.ValueKind == JsonValueKind.Object) result.Features[feature.Name] = ParameterValues.Read(feature.Value);
        return result;
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
    });
}

public interface IUserPreferencesStore
{
    string? Warning { get; }
    TrainerUserPreferences Load();
    void Save(TrainerUserPreferences preferences);
}

public sealed class FileUserPreferencesStore : IUserPreferencesStore
{
    private readonly string _path;
    private bool _unsupported;
    public string? Warning { get; private set; }
    public FileUserPreferencesStore(string path) { _path = Path.GetFullPath(path); }

    public TrainerUserPreferences Load()
    {
        Warning = null;
        _unsupported = false;
        foreach (var candidate in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var result = TrainerUserPreferences.Parse(File.ReadAllText(candidate));
                if (candidate != _path) Warning = "参数文件读取失败，已从备份恢复";
                return result;
            }
            catch (NotSupportedException)
            {
                _unsupported = true;
                Warning = "参数文件版本较新，已使用默认值；原文件保持不变";
                return new();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                Warning = "参数文件读取失败，已使用默认值；可用备份将自动恢复";
            }
        }
        return new();
    }

    public void Save(TrainerUserPreferences preferences)
    {
        if (_unsupported) throw new IOException("参数文件版本较新，未覆盖原文件");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(preferences.ToJson());
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_path))
            {
                // A broken primary must never replace a usable backup.
                bool primaryValid;
                try { TrainerUserPreferences.Parse(File.ReadAllText(_path)); primaryValid = true; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or NotSupportedException) { primaryValid = false; }
                File.Replace(temporary, _path, primaryValid ? _path + ".bak" : null);
            }
            else File.Move(temporary, _path);
            Warning = null;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
