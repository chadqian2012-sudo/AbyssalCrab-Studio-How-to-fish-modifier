using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;

namespace HowToFishTrainer.App;

public sealed partial class MainViewModel
{
    private TrainerUserPreferences _preferences = new();
    private IUserPreferencesStore? _preferencesStore;
    private readonly Dictionary<string, Dictionary<string, string>> _parameterDrafts = new(StringComparer.Ordinal);
    private readonly List<Action> _featureParameterUnsubscribe = new();
    private readonly List<Action> _parameterUnsubscribe = new();
    private DispatcherTimer? _preferencesTimer;
    private bool _restoringPreferences, _refreshingFeatureParameters, _preferencesDisposed;
    private bool _preferencesDirty;
    private string? _preferencesWarning;
    private string? _preferredIslandKey;

    private void PreferencesWarning(string? message)
    {
        _preferencesWarning = message;
        OnPropertyChanged(nameof(ModeStatus));
    }

    private void InitializePreferences(IUserPreferencesStore? store)
    {
        _preferencesStore = store;
        _restoringPreferences = true;
        try
        {
            if (store != null)
            {
                try { _preferences = store.Load(); PreferencesWarning(store.Warning); }
                catch (Exception e) { PreferencesWarning("参数读取失败 · " + e.Message); }
            }
            ApplyPreferences(_preferences);
        }
        finally { _restoringPreferences = false; }
        ObserveParameters(this, new[] { nameof(SoundEnabled), nameof(SelectedPage), nameof(TeleportPointQuery), nameof(SelectedTeleportGroup), nameof(SelectedTeleportPoint), nameof(PointGroup), nameof(PointName), nameof(SafeLanding), nameof(IslandQuery), nameof(SelectedIsland) });
        ObserveParameters(AimAssist, new[] { nameof(AimAssist.SelectedTrigger), nameof(AimAssist.LeadEnabled), nameof(AimAssist.HeadLockEnabled) });
        ObserveParameters(Crosshair, new[] { nameof(Crosshair.Shape), nameof(Crosshair.Color), nameof(Crosshair.Size) });
        ObserveParameters(FunEventsPage.Roulette, new[] { "SelectedOutcomeMode", "SelectedRoundMode", "UseOriginalMultiplier" });
        ObserveParameters(FunEventsPage.SlotMachine, new[] { "SelectedRarity", "TargetRarity" });
        ObserveParameters(RoomMembersPage, new[] { "HealthScope", "FullnessScope", "PoisonScope", "FireScope", "DropScope", "RareScope", "SearchText" });
        foreach (var input in new[] { CurrentXInput, CurrentYInput, CurrentZInput, AimAssist.StrengthInput, AimAssist.AngleInput, AimAssist.DistanceInput, FunEventsPage.Roulette.MultiplierInput })
            ObserveParameters(input, new[] { nameof(NumericInput.Text) });
        if (store != null)
        {
            _preferencesTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            _preferencesTimer.Tick += PreferencesTimerTick;
        }
    }

    private void ApplyPreferences(TrainerUserPreferences p)
    {
            SoundEnabled = p.General.Boolean("soundEnabled") ?? SoundEnabled;
            if(p.General.Text("spinCurve") is string curve && SpinCurves.Contains(curve)) SpinCurve=curve;
            RestoreNumber(p.General,"refillLimit",RefillLimitInput);
            var t = p.Teleport;
            TeleportPointQuery = t.Text("query") ?? TeleportPointQuery;
            string group = t.Text("selectedGroup") ?? SelectedTeleportGroup;
            if (TeleportGroups.Contains(group)) SelectedTeleportGroup = group;
            string? pointGroup = t.Text("selectedPointGroup"), pointName = t.Text("selectedPointName");
            SelectedTeleportPoint = TeleportPoints.FirstOrDefault(point => point.Group == pointGroup && point.Name == pointName);
            PointGroup = t.Text("pointGroup") ?? PointGroup; PointName = t.Text("pointName") ?? PointName;
            RestoreNumber(t, "x", CurrentXInput); RestoreNumber(t, "y", CurrentYInput); RestoreNumber(t, "z", CurrentZInput);
            SafeLanding = t.Boolean("safeLanding") ?? SafeLanding;
            IslandQuery = t.Text("islandQuery") ?? IslandQuery; _preferredIslandKey = t.Text("islandKey");
            var a = p.AimAssist;
            RestoreNumber(a, "strength", AimAssist.StrengthInput); RestoreNumber(a, "angle", AimAssist.AngleInput);
            RestoreNumber(a, "distance", AimAssist.DistanceInput);
            AimAssist.SelectedTrigger = AimAssist.TriggerModes.FirstOrDefault(o => o.Value == a.Text("trigger")) ?? AimAssist.SelectedTrigger;
            AimAssist.LeadEnabled = a.Boolean("leadEnabled") ?? AimAssist.LeadEnabled;
            AimAssist.HeadLockEnabled = a.Boolean("headLockEnabled") ?? AimAssist.HeadLockEnabled;
            var c = p.Crosshair;
            Crosshair.Shape = c.Text("shape") ?? Crosshair.Shape; Crosshair.Color = c.Text("color") ?? Crosshair.Color;
            if (c.Number("size") is double size && size >= 6 && size <= 36) Crosshair.Size = size;
            var r = p.Roulette;
            var roulette = FunEventsPage.Roulette;
            roulette.SelectedOutcomeMode = roulette.OutcomeModes.FirstOrDefault(o => o.Value == r.Text("outcome")) ?? roulette.SelectedOutcomeMode;
            roulette.SelectedRoundMode = roulette.RoundModes.FirstOrDefault(o => o.Rounds == r.Number("rounds")) ?? roulette.SelectedRoundMode;
            roulette.UseOriginalMultiplier = r.Boolean("useOriginalMultiplier") ?? roulette.UseOriginalMultiplier;
            RestoreNumber(r, "multiplier", roulette.MultiplierInput);
            if (p.SlotMachine.Number("rarity") is double rarity && FunEventsPage.SlotMachine.RarityOptions.Any(o => o.Value == rarity))
                FunEventsPage.SlotMachine.TargetRarity = (int)rarity;
            var members = RoomMembersPage; var m = p.RoomMembers;
            string Scope(string key, string fallback) => m.Text(key) is string scope && new[] { "off", "local", "selected", "all" }.Contains(scope) ? scope : fallback;
            members.HealthScope = Scope("healthScope", members.HealthScope); members.FullnessScope = Scope("fullnessScope", members.FullnessScope);
            members.PoisonScope = Scope("poisonScope", members.PoisonScope); members.FireScope = Scope("fireScope", members.FireScope);
            members.DropScope = Scope("dropScope", members.DropScope); members.RareScope = Scope("rareScope", members.RareScope);
            members.SearchText = m.Text("search") ?? members.SearchText;
            SelectedPage = Pages.FirstOrDefault(page => page.Category == p.General.Text("page")) ?? SelectedPage;
            RefreshFeatures();
            // Enabled states and session/player identifiers are deliberately absent from the model.
    }

    private static void RestoreNumber(ParameterValues values, string key, NumericInput input)
    {
        if (values.Number(key) is double number && input.IsValidValue(number)) input.Value = number;
    }

    private void ObserveParameters(INotifyPropertyChanged source, string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        PropertyChangedEventHandler handler = (sender, e) => {
            if (!set.Contains(e.PropertyName ?? "")) return;
            if (ReferenceEquals(sender, this) && e.PropertyName == nameof(SelectedIsland) && SelectedIsland != null)
                _preferredIslandKey = SelectedIsland.Key;
            SchedulePreferencesSave();
        };
        source.PropertyChanged += handler;
        _parameterUnsubscribe.Add(() => source.PropertyChanged -= handler);
    }

    private void CaptureFeatureParameters()
    {
        if (_restoringPreferences) return;
        foreach (var feature in Features)
        {
            _preferences.Features[feature.Descriptor.Id] = feature.CaptureParameters();
            _parameterDrafts[feature.Descriptor.Id] = feature.CaptureParameterDrafts();
        }
    }

    private void RestoreFeatureParameters(FeatureItemViewModel feature)
    {
        if (_preferences.Features.TryGetValue(feature.Descriptor.Id, out var saved))
        {
            _parameterDrafts.TryGetValue(feature.Descriptor.Id, out var drafts);
            feature.RestoreParameters(saved, drafts);
        }
    }

    private void ObserveFeatureParameters(FeatureItemViewModel feature)
    {
        var names = new HashSet<string>(new[] { "CatalogView", "SpinSpeedText", "SaveAfter", "DeadFish", "Rare", "SearchText", "SelectedKind", "SelectedOption", "SelectedSight", "SelectedBarrel", "SelectedAmmo", "SelectedSkin", "ExtendedMagazine", "LaserSight" }, StringComparer.Ordinal);
        PropertyChangedEventHandler handler = (_, e) => { if (names.Contains(e.PropertyName ?? "")) SchedulePreferencesSave(); };
        feature.PropertyChanged += handler;
        _featureParameterUnsubscribe.Add(() => feature.PropertyChanged -= handler);
        foreach (var input in feature.ParameterInputs.Values)
        {
            PropertyChangedEventHandler numeric = (_, e) => { if (e.PropertyName == nameof(NumericInput.Text)) SchedulePreferencesSave(); };
            input.PropertyChanged += numeric;
            _featureParameterUnsubscribe.Add(() => input.PropertyChanged -= numeric);
        }
    }

    private void DetachFeatureParameterObservers()
    {
        foreach (var unsubscribe in _featureParameterUnsubscribe) unsubscribe();
        _featureParameterUnsubscribe.Clear();
    }

    private void SchedulePreferencesSave()
    {
        if (_restoringPreferences || _refreshingFeatureParameters || _preferencesDisposed) return;
        ActivePresetName="自定义参数";
        _preferencesDirty = true;
        _preferencesTimer?.Stop(); _preferencesTimer?.Start();
    }

    private void PreferencesTimerTick(object? sender, EventArgs e) => FlushPreferences();

    public bool FlushPreferences()
    {
        _preferencesTimer?.Stop();
        if (_preferencesStore == null || !_preferencesDirty || _preferencesDisposed) return true;
        try
        {
            var p = CapturePreferences();
            _preferencesStore.Save(p);
            _preferencesDirty = false; PreferencesWarning(null); return true;
        }
        catch (Exception e) { PreferencesWarning("参数保存失败 · " + e.Message); return false; }
    }

    internal TrainerUserPreferences CapturePreferences()
    {
            CaptureFeatureParameters();
            var p = _preferences;
            p.General.Put("soundEnabled", SoundEnabled); p.General.Put("page", SelectedPage.Category);
            p.General.Put("spinCurve", SpinCurve);p.General.Put("refillLimit",RefillLimitInput.LastValidValue);
            var t = p.Teleport;
            t.Put("query", TeleportPointQuery); t.Put("selectedGroup", SelectedTeleportGroup);
            t.Put("selectedPointGroup", SelectedTeleportPoint?.Group); t.Put("selectedPointName", SelectedTeleportPoint?.Name);
            t.Put("pointGroup", PointGroup); t.Put("pointName", PointName);
            t.Put("x", CurrentXInput.LastValidValue); t.Put("y", CurrentYInput.LastValidValue); t.Put("z", CurrentZInput.LastValidValue);
            t.Put("safeLanding", SafeLanding); t.Put("islandQuery", IslandQuery); t.Put("islandKey", _preferredIslandKey);
            var a = p.AimAssist;
            a.Put("strength", AimAssist.StrengthInput.LastValidValue); a.Put("angle", AimAssist.AngleInput.LastValidValue);
            a.Put("distance", AimAssist.DistanceInput.LastValidValue); a.Put("trigger", AimAssist.SelectedTrigger.Value);
            a.Put("leadEnabled", AimAssist.LeadEnabled); a.Put("headLockEnabled", AimAssist.HeadLockEnabled);
            p.Crosshair.Put("shape", Crosshair.Shape); p.Crosshair.Put("color", Crosshair.Color); p.Crosshair.Put("size", Crosshair.Size);
            var roulette = FunEventsPage.Roulette; var r = p.Roulette;
            r.Put("outcome", roulette.SelectedOutcomeMode.Value); r.Put("rounds", roulette.SelectedRoundMode.Rounds);
            r.Put("useOriginalMultiplier", roulette.UseOriginalMultiplier); r.Put("multiplier", roulette.MultiplierInput.LastValidValue);
            p.SlotMachine.Put("rarity", FunEventsPage.SlotMachine.TargetRarity);
            var members = RoomMembersPage; var m = p.RoomMembers;
            m.Put("healthScope", members.HealthScope); m.Put("fullnessScope", members.FullnessScope);
            m.Put("poisonScope", members.PoisonScope); m.Put("fireScope", members.FireScope);
            m.Put("dropScope", members.DropScope); m.Put("rareScope", members.RareScope); m.Put("search", members.SearchText);
            return TrainerUserPreferences.Parse(p.ToJson());
    }

    public void ClosePreferences()
    {
        if (_preferencesDisposed) return;
        FlushPreferences();
        _preferencesDisposed = true;
        if (_preferencesTimer != null) { _preferencesTimer.Stop(); _preferencesTimer.Tick -= PreferencesTimerTick; }
        DetachFeatureParameterObservers();
        foreach (var unsubscribe in _parameterUnsubscribe) unsubscribe();
        _parameterUnsubscribe.Clear();
    }
}
