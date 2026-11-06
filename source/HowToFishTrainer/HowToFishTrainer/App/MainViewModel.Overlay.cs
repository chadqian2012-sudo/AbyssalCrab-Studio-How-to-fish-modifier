using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed record OverlayFeatureChoice(string Id, string Title, string Category, bool IsPage = false);

public sealed class OverlayShortcutViewModel
{
    private readonly MainViewModel _owner;
    internal OverlayShortcut Reference { get; }
    public string Kind => Reference.Kind;
    public string Title { get; }
    public string KindLabel { get; }
    public string ActionLabel => Kind switch { "teleport" => "传送", "preset" => "恢复参数", _ => "打开设置" };
    public bool IsAvailable { get; }
    public FeatureItemViewModel? SourceFeature { get; }
    public FeatureItemViewModel? Feature { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    internal OverlayShortcutViewModel(MainViewModel owner, OverlayPanel panel, OverlayShortcut reference)
    {
        _owner = owner; Reference = reference;
        var feature = owner.AllFeatures.FirstOrDefault(f => f.Descriptor.Id == reference.Key);
        SourceFeature = reference.Kind == "feature" ? feature : null;
        var page = owner.Pages.FirstOrDefault(p => p.Category == reference.Key);
        var point = owner.TeleportPoints.FirstOrDefault(p => p.Group == reference.Group && p.Name == reference.Key);
        var preset = owner.Presets.FirstOrDefault(p => p.Name == reference.Key);
        IsAvailable = reference.Kind switch { "feature" => feature != null, "page" => page != null, "teleport" => point != null, "preset" => preset != null, _ => false };
        KindLabel = reference.Kind switch { "feature" => "功能", "page" => "完整设置", "teleport" => "传送自己", "preset" => "载入参数", _ => "不可用" };
        Title = (reference.Kind switch { "feature" => feature?.Descriptor.Title, "page" => page?.Title, "teleport" => point?.Name, "preset" => preset?.Name, _ => null } ?? reference.Key) + (IsAvailable ? "" : " · 已失效");
        if (reference.Kind == "feature" && feature != null && !feature.IsSpin && !feature.IsMagicBullet &&
            feature.Descriptor.Control is FeatureControl.Toggle or FeatureControl.Number or FeatureControl.Action)
            Feature = feature;
        ExecuteCommand = new RelayCommand(() => _ = ExecuteAsync());
        OpenCommand = new RelayCommand(() => owner.OpenOverlayReference(reference));
        RemoveCommand = new RelayCommand(() => owner.RemoveOverlayItem(panel, reference));
        MoveUpCommand = new RelayCommand(() => owner.MoveOverlayItem(panel, reference, -1));
        MoveDownCommand = new RelayCommand(() => owner.MoveOverlayItem(panel, reference, 1));
    }

    public Task ExecuteAsync()
    {
        if (!IsAvailable) { _owner.OverlayStatus = "该快捷项已失效，请重新选择"; return Task.CompletedTask; }
        return Reference.Kind switch {
            "teleport" => _owner.TeleportSavedPointAsync(Reference.Group, Reference.Key),
            "preset" => _owner.LoadOverlayPresetAsync(Reference.Key),
            _ => Open()
        };
    }
    private Task Open() { _owner.OpenOverlayReference(Reference); return Task.CompletedTask; }
}

public sealed partial class MainViewModel
{
    private OverlayPanel? _selectedOverlayPanel;
    private OverlayFeatureChoice? _selectedOverlayFeatureChoice;
    private string _overlayPanelName = "常用", _overlayStatus = "Home 展开或收起 · 拖动标题可移动";
    public ObservableCollection<OverlayPanel> OverlayPanels { get; } = new();
    public event Action<string>? OverlayNavigationRequested;
    public OverlayPanel? SelectedOverlayPanel
    {
        get => _selectedOverlayPanel;
        set {
            if (value != null && !OverlayPanels.Contains(value)) return;
            if (!Set(ref _selectedOverlayPanel, value)) return;
            _experience.SelectedOverlayPanelId = value?.Id;
            OverlayPanelName = value?.Name ?? "";
            ScheduleExperienceSave();
        }
    }
    public string OverlayPanelName { get => _overlayPanelName; set => Set(ref _overlayPanelName, value ?? ""); }
    public string OverlayStatus { get => _overlayStatus; set => Set(ref _overlayStatus, value ?? ""); }
    public string OverlayHotkey
    {
        get => _experience.OverlayHotkey;
        set { value = (value ?? "").Trim(); if (value.Length > 60 || value == _experience.OverlayHotkey) return; _experience.OverlayHotkey = value; OnPropertyChanged(); ScheduleExperienceSave(); }
    }
    public double WidgetWidth { get => _experience.WidgetWidth; set { if (double.IsFinite(value) && value >= 360 && value <= 3840) { _experience.WidgetWidth = value; ScheduleExperienceSave(); } } }
    public double WidgetHeight { get => _experience.WidgetHeight; set { if (double.IsFinite(value) && value >= 280 && value <= 2160) { _experience.WidgetHeight = value; ScheduleExperienceSave(); } } }
    public IReadOnlyList<OverlayFeatureChoice> OverlayFeatureChoices => AllFeatures.Select(f => new OverlayFeatureChoice(f.Descriptor.Id, f.Descriptor.Title, DisplayCategory(f.Descriptor.Id, f.Descriptor.Category)))
        .Concat(Pages.Select(p => new OverlayFeatureChoice(p.Category, p.Title + " · 完整设置", p.Category, true))).ToArray();
    public OverlayFeatureChoice? SelectedOverlayFeatureChoice { get => _selectedOverlayFeatureChoice; set => Set(ref _selectedOverlayFeatureChoice, value); }
    public ICommand CreateOverlayPanelCommand => new RelayCommand(CreateOverlayPanel);
    public ICommand RenameOverlayPanelCommand => new RelayCommand(RenameOverlayPanel);
    public ICommand DeleteOverlayPanelCommand => new RelayCommand(DeleteOverlayPanel);
    public ICommand AddFeatureToOverlayCommand => new RelayCommand(parameter => {
        var choice = parameter as OverlayFeatureChoice ?? SelectedOverlayFeatureChoice;
        if (parameter is SearchHit hit) {
            if (ResolveOverlaySearchReference(hit) is { } reference) AddOverlayReference(reference);
        }
        else if (parameter is FeatureItemViewModel feature) AddOverlayReference(new("feature", feature.Descriptor.Id));
        else if (choice != null) AddOverlayReference(new(choice.IsPage ? "page" : "feature", choice.Id));
    });
    public ICommand OpenOverlaySearchCommand => new RelayCommand(parameter => {
        if (parameter is SearchHit hit && ResolveOverlaySearchReference(hit) is { } reference) OpenOverlayReference(reference);
    });
    public ICommand AddTeleportToOverlayCommand => new RelayCommand(parameter => {
        var point = parameter as TrainerTeleportPoint ?? SelectedTeleportPoint;
        if (point != null) AddOverlayReference(new("teleport", point.Name, point.Group));
        else OverlayStatus = "请先选择一个已保存的传送点";
    });
    public ICommand AddPresetToOverlayCommand => new RelayCommand(parameter => {
        var preset = parameter as ParameterPreset ?? SelectedPreset;
        if (preset != null) AddOverlayReference(new("preset", preset.Name));
        else OverlayStatus = "请先选择参数预设";
    });
    public ICommand TeleportSavedPointCommand => new RelayCommand(parameter => { if (parameter is TrainerTeleportPoint point) _ = TeleportSavedPointAsync(point.Group, point.Name); });
    public ICommand LoadOverlayPresetCommand => new RelayCommand(parameter => { if (parameter is ParameterPreset preset) _ = LoadOverlayPresetAsync(preset.Name); });
    public ICommand EmergencyDisableCommand => new RelayCommand(() => _ = EmergencyDisableAsync());
    public ICommand ReadLocalPositionCommand => new RelayCommand(() => {
        if (!CanPositionAction) return;
        TargetRoomMember = false; SelectedTeleportPoint = null;
        _ = RunPositionActionAsync(ReadPositionAsync);
    });
    public ICommand RenamePresetCommand => new RelayCommand(() => {
        if (SelectedPreset == null || PresetBusy) return;
        string name = PresetName.Trim();
        if (name.Length == 0 || name.Length > 40 || Presets.Any(p => p != SelectedPreset && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        { ExperienceStatus = "预设名称需要 1–40 个字符，且不能重复"; return; }
        var old = SelectedPreset; var replacement = old with { Name = name };
        int index = Presets.IndexOf(old); Presets[index] = replacement;
        _experience.Presets[_experience.Presets.IndexOf(old)] = replacement;
        ReplaceOverlayReference(new("preset", old.Name), new("preset", name));
        SelectedPreset = replacement; ScheduleExperienceSave(); ExperienceStatus = "预设已重命名";
    });

    private void InitializeOverlay()
    {
        if (_experience.OverlayPanels.Count == 0) { _experience.OverlayPanels.Add(new()); ScheduleExperienceSave(); }
        foreach (var panel in _experience.OverlayPanels) OverlayPanels.Add(panel);
        SelectedOverlayPanel = OverlayPanels.FirstOrDefault(p => p.Id == _experience.SelectedOverlayPanelId) ?? OverlayPanels.First();
        TeleportPoints.CollectionChanged += (_, _) => RefreshOverlayItems();
        Presets.CollectionChanged += (_, _) => RefreshOverlayItems();
        RefreshOverlayItems();
    }
    private bool ValidOverlayName(out string name)
    {
        name = OverlayPanelName.Trim();
        if (name.Length > 0 && name.Length <= 40) return true;
        OverlayStatus = "面板名称需要 1–40 个字符"; return false;
    }
    private void CreateOverlayPanel()
    {
        if (!ValidOverlayName(out string name)) return;
        if (OverlayPanels.Count >= 50) { OverlayStatus = "最多保存 50 套快捷面板"; return; }
        if (OverlayPanels.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) { OverlayStatus = "已有同名面板"; return; }
        var panel = new OverlayPanel { Name = name };
        _experience.OverlayPanels.Add(panel); OverlayPanels.Add(panel); SelectedOverlayPanel = panel;
        ScheduleExperienceSave(); OverlayStatus = "已创建「" + name + "」";
    }
    private void RenameOverlayPanel()
    {
        if (SelectedOverlayPanel == null || !ValidOverlayName(out string name)) return;
        if (OverlayPanels.Any(p => p != SelectedOverlayPanel && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) { OverlayStatus = "已有同名面板"; return; }
        SelectedOverlayPanel.Name = name; ScheduleExperienceSave(); OverlayStatus = "面板已重命名";
    }
    private void DeleteOverlayPanel()
    {
        var panel = SelectedOverlayPanel; if (panel == null) return;
        if (OverlayPanels.Count == 1) { OverlayStatus = "请至少保留一套面板；可以移除其中的快捷项"; return; }
        _experience.OverlayPanels.Remove(panel); OverlayPanels.Remove(panel); SelectedOverlayPanel = OverlayPanels.First();
        ScheduleExperienceSave(); OverlayStatus = "已删除面板「" + panel.Name + "」";
    }
    private void AddOverlayReference(OverlayShortcut item)
    {
        var panel = SelectedOverlayPanel; if (panel == null) return;
        if (panel.Shortcuts.Contains(item)) { OverlayStatus = "当前面板已包含此快捷项"; return; }
        if (panel.Shortcuts.Count >= 200) { OverlayStatus = "每套面板最多保存 200 个快捷项"; return; }
        panel.Shortcuts.Add(item); RefreshOverlayItems(); ScheduleExperienceSave(); OverlayStatus = "已添加到「" + panel.Name + "」";
    }
    internal void RemoveOverlayItem(OverlayPanel panel, OverlayShortcut reference)
    {
        if (!OverlayPanels.Contains(panel)) return;
        panel.Shortcuts.Remove(reference); RefreshOverlayItems(); ScheduleExperienceSave();
    }
    internal void MoveOverlayItem(OverlayPanel panel, OverlayShortcut reference, int direction)
    {
        if (!OverlayPanels.Contains(panel)) return;
        int index = panel.Shortcuts.IndexOf(reference), target = index + direction;
        if (index < 0 || target < 0 || target >= panel.Shortcuts.Count) return;
        panel.Shortcuts.RemoveAt(index); panel.Shortcuts.Insert(target, reference);
        RefreshOverlayItems(); ScheduleExperienceSave();
    }
    private void RefreshOverlayItems()
    {
        foreach (var panel in OverlayPanels) {
            panel.Items.Clear();
            foreach (var shortcut in panel.Shortcuts) panel.Items.Add(new(this, panel, shortcut));
        }
        OnPropertyChanged(nameof(OverlayFeatureChoices));
    }
    private void ReplaceOverlayReference(OverlayShortcut previous, OverlayShortcut replacement)
    {
        foreach (var panel in OverlayPanels)
            for (int i = 0; i < panel.Shortcuts.Count; i++)
                if (panel.Shortcuts[i] == previous) panel.Shortcuts[i] = replacement;
        RefreshOverlayItems(); ScheduleExperienceSave();
    }
    private void RemoveOverlayReference(OverlayShortcut reference)
    {
        foreach (var panel in OverlayPanels) panel.Shortcuts.RemoveAll(p => p == reference);
        RefreshOverlayItems(); ScheduleExperienceSave();
    }
    private void RenameOverlayTeleportReference(TrainerTeleportPoint? previous, TrainerTeleportPoint replacement)
    {
        if (previous != null) ReplaceOverlayReference(new("teleport", previous.Name, previous.Group), new("teleport", replacement.Name, replacement.Group));
    }
    private OverlayShortcut? ResolveOverlaySearchReference(SearchHit hit)
    {
        if (AllFeatures.Any(f => f.Descriptor.Id == hit.Id)) return new("feature", hit.Id);
        var descriptor = FeatureRegistry.All.FirstOrDefault(f => f.Id == hit.Id);
        if (descriptor != null && (TeleportCustomFeatureIds.Contains(hit.Id) || CombatCustomFeatureIds.Contains(hit.Id) ||
            hit.Id is "fishing.skin_jackpot" or "fun.slot_machine.rarity")) {
            string category = DisplayCategory(descriptor.Id, descriptor.Category);
            if (Pages.Any(p => p.Category == category)) return new("page", category);
        }
        OverlayStatus = "该搜索结果已失效，请重新搜索";
        return null;
    }
    internal void OpenOverlayReference(OverlayShortcut reference)
    {
        string? category = reference.Kind switch {
            "feature" => AllFeatures.FirstOrDefault(f => f.Descriptor.Id == reference.Key) is { } f ? DisplayCategory(f.Descriptor.Id, f.Descriptor.Category) : null,
            "page" => Pages.FirstOrDefault(p => p.Category == reference.Key)?.Category,
            "teleport" => "世界传送", "preset" => "@presets", _ => null
        };
        if (category == null) { OverlayStatus = "该快捷项已失效，请重新选择"; return; }
        if (category != "@presets") SelectedPage = Pages.First(p => p.Category == category);
        OverlayNavigationRequested?.Invoke(category);
    }
    public Task TeleportSavedPointAsync(string group, string name) => RunPositionActionAsync(async () => {
        var point = TeleportPoints.FirstOrDefault(p => p.Group == group && p.Name == name);
        if (point == null) { PositionStatus = "传送点已删除，请重新选择"; return; }
        await TeleportToCoordinatesAsync(point.X, point.Y, point.Z, null);
    });
    public async Task LoadOverlayPresetAsync(string name)
    {
        var preset = Presets.FirstOrDefault(p => p.Name == name);
        if (preset == null) { OverlayStatus = "预设已删除，请重新选择"; return; }
        if (PresetBusy) { OverlayStatus = "预设操作进行中"; return; }
        SelectedPreset = preset;
        var operation = ApplySelectedPresetAsync();
        int version = _presetApplyVersion, epoch = _connectionEpoch, context = _positionContextEpoch;
        await operation;
        if (version == _presetApplyVersion && epoch == _connectionEpoch && context == _positionContextEpoch)
            OverlayStatus = ExperienceStatus;
    }
}
