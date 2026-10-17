using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HowToFishTrainer.App;

public sealed record ParameterPreset(string Name, string Parameters);
public sealed record CatalogBookmark(string FeatureId, int Id, string Name, ParameterValues Parameters);
public sealed record PointDecoration(string Group, string Name, string Color, string Icon, string Note, bool Favorite);
public sealed record SearchHit(string Title, string Category, string Id, string Description);
public sealed class ExperienceSettings
{
    public int Version { get; set; } = 1;
    public List<ParameterPreset> Presets { get; set; } = new();
    public List<CatalogBookmark> Favorites { get; set; } = new();
    public List<CatalogBookmark> Recent { get; set; } = new();
    public List<string> Searches { get; set; } = new();
    public List<PointDecoration> Points { get; set; } = new();
    public string PointSort { get; set; } = "收藏优先";
    public bool CompactNavigation { get; set; }
    public bool WidgetVisible { get; set; }
    public double WidgetLeft { get; set; } = 24;
    public double WidgetTop { get; set; } = 120;
    public string SpinCurve { get; set; } = "固定";
    public int RefillLimit { get; set; } = 3;
}

// A dedicated file keeps collections/presets separate from per-field numeric drafts.
// Reads never rewrite a file; unsupported future versions remain protected.
public sealed class ExperienceSettingsStore
{
    private readonly string? _path;
    private bool _unsupported;
    public string? Warning { get; private set; }
    public ExperienceSettingsStore(string? path) { _path = path == null ? null : Path.GetFullPath(path); }
    public static ExperienceSettings Parse(string text)
    {
        var value = JsonSerializer.Deserialize<ExperienceSettings>(text) ?? throw new JsonException("体验设置为空");
        if (value.Version != 1) throw new NotSupportedException("体验设置版本较新");
        value.Presets = (value.Presets ?? new()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 40)
            .Where(p => ValidPreset(p.Parameters)).DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        List<CatalogBookmark> Bookmarks(List<CatalogBookmark>? entries) => (entries ?? new()).Where(e => e != null && e.Parameters != null && e.Name != null && e.Name.Length <= 256
            && e.Id >= 0 && e.FeatureId is "generation.item" or "generation.fish").DistinctBy(e => (e.FeatureId,e.Id)).Take(200).ToList();
        value.Favorites = Bookmarks(value.Favorites); value.Recent = Bookmarks(value.Recent).Take(30).ToList();
        value.Searches = (value.Searches ?? new()).Where(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 100).Distinct().Take(12).ToList();
        value.Points = (value.Points ?? new()).Where(p => p != null && p.Group != null && p.Name != null && p.Note != null && p.Note.Length <= 500)
            .DistinctBy(p => (p.Group,p.Name)).Take(2000).ToList();
        if (!double.IsFinite(value.WidgetLeft)) value.WidgetLeft = 24;
        if (!double.IsFinite(value.WidgetTop)) value.WidgetTop = 120;
        if (value.RefillLimit < 1 || value.RefillLimit > 20) value.RefillLimit = 3;
        if (value.SpinCurve is not ("固定" or "缓慢加速" or "脉冲")) value.SpinCurve = "固定";
        return value;
    }
    private static bool ValidPreset(string text) { try { TrainerUserPreferences.Parse(text); return true; } catch { return false; } }
    public ExperienceSettings Load()
    {
        if (_path == null) return new();
        foreach (string path in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try { var result = Parse(File.ReadAllText(path)); if (path != _path) Warning = "体验设置已从备份恢复"; return result; }
            catch (NotSupportedException) { _unsupported = true; Warning = "体验设置版本较新，原文件保持不变"; return new(); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { Warning = "体验设置读取失败，尝试备份"; }
        }
        return new();
    }
    public void Save(ExperienceSettings value)
    {
        if (_path == null) return;
        if (_unsupported) throw new IOException("体验设置版本较新，未覆盖原文件");
        var text = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        Parse(text);
        AtomicLocalFile.Write(_path, text, previous => { Parse(previous); });
        Warning = null;
    }
}

internal static class AtomicLocalFile
{
    internal static void Write(string path, string text, Action<string> validate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(Encoding.UTF8.GetBytes(text)); stream.Flush(true); }
            if (!File.Exists(path)) File.Move(temp, path);
            else
            {
                bool valid;
                try { validate(File.ReadAllText(path)); valid = true; } catch { valid = false; }
                File.Replace(temp, path, valid ? path + ".bak" : null);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
