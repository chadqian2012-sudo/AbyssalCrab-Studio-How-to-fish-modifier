using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HowToFishTrainer.App;

public sealed record ParameterPreset(string Name, string Parameters);
public sealed record CatalogBookmark(string FeatureId, int Id, string Name, ParameterValues Parameters);
public sealed record PointDecoration(string Group, string Name, string Color, string Icon, string Note, bool Favorite);
public sealed record OverlayShortcut(string Kind, string Key, string Group = "");
public sealed class OverlayPanel : INotifyPropertyChanged
{
    private string _name = "常用";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public List<OverlayShortcut> Shortcuts { get; set; } = new();
    [JsonIgnore] public ObservableCollection<OverlayShortcutViewModel> Items { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class ExperienceSettings
{
    public int Version { get; set; } = 2;
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
    public double WidgetWidth { get; set; } = 620;
    public double WidgetHeight { get; set; } = 680;
    public string OverlayHotkey { get; set; } = "Home";
    public List<OverlayPanel> OverlayPanels { get; set; } = new();
    public string? SelectedOverlayPanelId { get; set; }
    public string SpinCurve { get; set; } = "固定";
    public int RefillLimit { get; set; } = 3;
    public AnimalCountSettings AnimalCounts { get; set; } = new();
    public CombatFeedbackOptions CombatFeedback { get; set; } = new();
}

// A dedicated file keeps collections/presets separate from per-field numeric drafts.
// Reads never rewrite a file; unsupported future versions remain protected.
public sealed class ExperienceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string? _path;
    private bool _unsupported;
    public string? Warning { get; private set; }
    public ExperienceSettingsStore(string? path) { _path = path == null ? null : Path.GetFullPath(path); }
    public static ExperienceSettings Parse(string text)
    {
        var value = JsonSerializer.Deserialize<ExperienceSettings>(text) ?? throw new JsonException("体验设置为空");
        if (value.Version is not (1 or 2)) throw new NotSupportedException("体验设置版本较新");
        value.Version = 2;
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
        if (!double.IsFinite(value.WidgetWidth) || value.WidgetWidth < 360 || value.WidgetWidth > 3840) value.WidgetWidth = 620;
        if (!double.IsFinite(value.WidgetHeight) || value.WidgetHeight < 280 || value.WidgetHeight > 2160) value.WidgetHeight = 680;
        if (string.IsNullOrWhiteSpace(value.OverlayHotkey) || value.OverlayHotkey.Length > 60) value.OverlayHotkey = "Home";
        value.OverlayPanels = (value.OverlayPanels ?? new()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 40)
            .DistinctBy(p => p.Id, StringComparer.Ordinal).Take(50).ToList();
        foreach (var panel in value.OverlayPanels)
            panel.Shortcuts = (panel.Shortcuts ?? new()).Where(p => p != null && p.Kind is "feature" or "page" or "teleport" or "preset"
                && !string.IsNullOrWhiteSpace(p.Key) && p.Key.Length <= 256 && p.Group != null && p.Group.Length <= 256).Distinct().Take(200).ToList();
        if (value.RefillLimit < 1 || value.RefillLimit > 20) value.RefillLimit = 3;
        value.AnimalCounts??=new();
        foreach(string key in value.AnimalCounts.Where(p=>!FunEventDefinitions.All.Any(d=>d.Id==p.Key&&d.HasAnimalCount)||p.Value<1||p.Value>50).Select(p=>p.Key).ToArray())value.AnimalCounts.Remove(key);
        value.CombatFeedback ??= new();
        if (value.SpinCurve is not ("固定" or "缓慢加速" or "脉冲")) value.SpinCurve = "固定";
        return value;
    }
    private static bool ValidPreset(string text) { try { TrainerUserPreferences.Parse(text); return true; } catch { return false; } }
    public ExperienceSettings Load()
    {
        Warning = null;
        _unsupported = false;
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
        try
        {
            if (_unsupported) throw new IOException("体验设置版本较新，未覆盖原文件");
            var text = JsonSerializer.Serialize(value, JsonOptions);
            Parse(text);
            AtomicLocalFile.Write(_path, text, previous => { Parse(previous); });
            Warning = null;
        }
        catch (Exception e) { Warning = "体验设置保存失败 · " + e.Message; throw; }
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
                try { validate(File.ReadAllText(path)); valid = true; }
                catch (NotSupportedException e) { throw new IOException("文件版本较新，未覆盖原文件", e); }
                catch (Exception e) when (e is JsonException or InvalidOperationException) { valid = false; }
                // Read/permission failures must abort instead of being treated as broken JSON.
                File.Replace(temp, path, valid ? path + ".bak" : null);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
