using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class MainViewModel
{
    private TrainerUserPreferences _preferences = new();
    private TrainerUserPreferences? _lastSessionPreferences;
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
            if (_preferences.Session.Boolean("snapshot") == true) _lastSessionPreferences = _preferences.Copy();
        }
        finally { _restoringPreferences = false; }
        OnPropertyChanged(nameof(CanRestoreLastSession));
        ObserveParameters(this, new[] { nameof(SoundEnabled), nameof(SelectedPage), nameof(TeleportPointQuery), nameof(SelectedTeleportGroup), nameof(SelectedTeleportPoint), nameof(PointGroup), nameof(PointName), nameof(SafeLanding), nameof(IslandQuery), nameof(SelectedIsland), nameof(SpinCurve) });
        ObserveParameters(AimAssist, new[] { nameof(AimAssist.SelectedTrigger), nameof(AimAssist.LeadEnabled), nameof(AimAssist.HeadLockEnabled), nameof(AimAssist.TargetTeammates) });
        ObserveParameters(Crosshair, new[] { nameof(Crosshair.Shape), nameof(Crosshair.Color), nameof(Crosshair.Size) });
        ObserveParameters(FunEventsPage.Roulette, new[] { "SelectedOutcomeMode", "SelectedRoundMode", "UseOriginalMultiplier" });
        ObserveParameters(FunEventsPage.SlotMachine, new[] { "SelectedRarity", "TargetRarity" });
        ObserveParameters(RoomMembersPage, new[] { "HealthScope", "FullnessScope", "PoisonScope", "FireScope", "DropScope", "RareScope", "SearchText" });
        foreach (var input in new[] { CurrentXInput, CurrentYInput, CurrentZInput, AimAssist.StrengthInput, AimAssist.AngleInput, AimAssist.DistanceInput, FunEventsPage.Roulette.MultiplierInput, RefillLimitInput, EntityOverlayDistanceInput })
            ObserveParameters(input, new[] { nameof(NumericInput.Text) });
        foreach (var card in FunEventsPage.Events.Where(card => card.HasAnimalCount))
            ObserveParameters(card.CountInput, new[] { nameof(NumericInput.Text) });
        CombatFeedback.SettingsChanged += SchedulePreferencesSave;
        _parameterUnsubscribe.Add(() => CombatFeedback.SettingsChanged -= SchedulePreferencesSave);
        if (store != null)
        {
            _preferencesTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            _preferencesTimer.Tick += PreferencesTimerTick;
        }
    }

    private void ApplyPreferences(TrainerUserPreferences p)
    {
            SoundEnabled = p.General.Boolean("soundEnabled") ?? SoundEnabled;
            if(p.General.TryGetValue("combatFeedback",out var feedback)&&feedback.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                CombatFeedback.Load(System.Text.Json.JsonSerializer.Deserialize<CombatFeedbackOptions>(feedback.GetRawText()));
            }
            if(p.General.TryGetValue("animalCounts",out var animalCounts)&&animalCounts.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                var counts=System.Text.Json.JsonSerializer.Deserialize<AnimalCountSettings>(animalCounts.GetRawText());
                foreach(var card in FunEventsPage.Events.Where(c=>c.HasAnimalCount))if(counts!=null&&counts.TryGetValue(card.Id,out int count))card.CountInput.Value=count;
            }
            if(p.General.Text("spinCurve") is string curve && SpinCurves.Contains(curve)) SpinCurve=curve;
            RestoreNumber(p.General,"refillLimit",RefillLimitInput);
            RestoreNumber(p.General,"entityOverlayDistance",EntityOverlayDistanceInput);
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
            AimAssist.TargetTeammates = a.Boolean("targetTeammates") ?? false;
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
            RebuildFeatureModels();
            // Session switch intentions are restored only by an explicit user command.
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
        foreach (var feature in AllFeatures)
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
        var names = new HashSet<string>(new[] { "CatalogView", "SpinSpeedText", "SpinAutoJump", "MagicTrigger", "MagicTargetTeammates", "SaveAfter", "DeadFish", "Rare", "SearchText", "SelectedKind", "SelectedOption", "SelectedSight", "SelectedBarrel", "SelectedAmmo", "SelectedSkin", "ExtendedMagazine", "LaserSight" }, StringComparer.Ordinal);
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
            p.General.Put("entityOverlayDistance",EntityOverlayDistanceInput.LastValidValue);
            p.General.Put("combatFeedback",CombatFeedback.Capture());
            p.General.Put("animalCounts",FunEventsPage.Events.Where(c=>c.HasAnimalCount).ToDictionary(c=>c.Id,c=>c.RequestedCount));
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
            a.Put("targetTeammates", AimAssist.TargetTeammates);
            p.Crosshair.Put("shape", Crosshair.Shape); p.Crosshair.Put("color", Crosshair.Color); p.Crosshair.Put("size", Crosshair.Size);
            var roulette = FunEventsPage.Roulette; var r = p.Roulette;
            r.Put("outcome", roulette.SelectedOutcomeMode.Value); r.Put("rounds", roulette.SelectedRoundMode.Rounds);
            r.Put("useOriginalMultiplier", roulette.UseOriginalMultiplier); r.Put("multiplier", roulette.MultiplierInput.LastValidValue);
            p.SlotMachine.Put("rarity", FunEventsPage.SlotMachine.TargetRarity);
            var members = RoomMembersPage; var m = p.RoomMembers;
            m.Put("healthScope", members.HealthScope); m.Put("fullnessScope", members.FullnessScope);
            m.Put("poisonScope", members.PoisonScope); m.Put("fireScope", members.FireScope);
            m.Put("dropScope", members.DropScope); m.Put("rareScope", members.RareScope); m.Put("search", members.SearchText);
            CaptureSessionSwitches();
            return p.Copy();
    }

    private void CaptureSessionSwitches()
    {
        if (_runtime == null || PresetBusy || _closedExperience || _preferencesDisposed) return;
        var s = _preferences.Session;
        s.Put("snapshot", true);
        foreach (var descriptor in FeatureRegistry.All.Where(f => f.Control == FeatureControl.Toggle))
            s.Put("toggle:" + descriptor.Id, _toggleStates.TryGetValue(descriptor.Id, out var state) && state.Enabled);
        s.Put("aimAssist", AimAssist.Enabled); s.Put("crosshair", Crosshair.Enabled);
        s.Put("combatFeedback", CombatFeedback.Active); s.Put("slotMachine", FunEventsPage.SlotMachine.Enabled);
        s.Put("roulette", FunEventsPage.Roulette.Active); s.Put("autoRefill", AutoRefill);
        s.Put("trackDeathPosition", TrackDeathPosition); s.Put("diagnosticOnly", DiagnosticOnly);
        s.Put("widgetVisible", WidgetVisible); s.Put("compactNavigation", CompactNavigation);
    }

    public bool CanRestoreLastSession => _lastSessionPreferences != null && _runtime != null &&
        _capabilities.CanExecute(Capability.OwnedPlayer) && !PresetBusy && !_closedExperience && !_preferencesDisposed;
    public ICommand RestoreLastSessionCommand => new RelayCommand(() => _ = RestoreLastSessionAsync());
    public Task RestoreLastSessionAsync() => CanRestoreLastSession ?
        ApplySavedPreferencesAsync(_lastSessionPreferences!.ToJson(), "上次会话", true) : Task.CompletedTask;

    private async Task<List<string>> RestoreSessionSwitchesAsync(TrainerUserPreferences parameters,
        IGameRuntimeClient runtime, Func<bool> current)
    {
        var failures = new List<string>();
        var session = parameters.Session.Copy();
        DiagnosticOnly = session.Boolean("diagnosticOnly") == true;
        foreach (var descriptor in FeatureRegistry.All.Where(f => f.Control == FeatureControl.Toggle))
        {
            if (!current()) return failures;
            if (session.Boolean("toggle:" + descriptor.Id) != true || DiagnosticOnly && descriptor.Id == "combat.silent_aim") continue;
            var feature = new FeatureItemViewModel(descriptor, runtime, _capabilities,
                stateChanged: source => { if (current()) RememberToggleState(source); }, sponsorAccessGate: _sponsorAccessGate, entityOverlayDistanceInput: EntityOverlayDistanceInput);
            if (parameters.Features.TryGetValue(descriptor.Id, out var values)) feature.RestoreParameters(values);
            await feature.SetEnabledAsync(true);
            if (!current()) return failures;
            if (!feature.Enabled) failures.Add(descriptor.Title + "恢复失败 · " + feature.Status);
        }

        // Only persistent multipliers are applied. Numeric actions such as currency changes are never replayed.
        foreach (var descriptor in FeatureRegistry.All.Where(f => f.Control == FeatureControl.Number && f.Id.EndsWith(".multiplier", StringComparison.Ordinal)))
        {
            if (!current()) return failures;
            if (!parameters.Features.TryGetValue(descriptor.Id, out var values) || values.Number("value") is not double value) continue;
            var feature = new FeatureItemViewModel(descriptor, runtime, _capabilities, sponsorAccessGate: _sponsorAccessGate);
            if (!feature.ValueInput.IsValidValue(value)) { failures.Add(descriptor.Title + "数值无效"); continue; }
            feature.ValueInput.Value = value;
            await feature.Execute(runtime, _capabilities);
            if (!current()) return failures;
            if (!feature.IsSuccessful) failures.Add(descriptor.Title + "恢复失败 · " + feature.Status);
        }
        if (session.Boolean("aimAssist") == true && !DiagnosticOnly)
        {
            await AimAssist.SetEnabledAsync(true);
            if (!current()) return failures;
            if (!AimAssist.Enabled) failures.Add(AimAssist.Status);
        }
        Crosshair.Enabled = session.Boolean("crosshair") == true;
        if (session.Boolean("combatFeedback") == true)
        {
            await CombatFeedback.ApplyAsync();
            if (!current()) return failures;
            if (!CombatFeedback.Active) failures.Add(CombatFeedback.Status);
        }
        if (session.Boolean("slotMachine") == true)
        {
            await FunEventsPage.SlotMachine.ApplyAsync();
            if (!current()) return failures;
            if (!FunEventsPage.SlotMachine.Enabled) failures.Add(FunEventsPage.SlotMachine.Status);
        }
        if (session.Boolean("roulette") == true)
        {
            await FunEventsPage.Roulette.ApplyAsync();
            if (!current()) return failures;
            if (!FunEventsPage.Roulette.Active) failures.Add(FunEventsPage.Roulette.Status);
        }
        TrackDeathPosition = session.Boolean("trackDeathPosition") == true;
        AutoRefill = session.Boolean("autoRefill") == true;
        if (session.Boolean("autoRefill") == true && !AutoRefill) failures.Add(AutoRefillStatus);
        WidgetVisible = session.Boolean("widgetVisible") ?? WidgetVisible;
        CompactNavigation = session.Boolean("compactNavigation") ?? CompactNavigation;
        return failures;
    }

    public void ClosePreferences()
    {
        if (_preferencesDisposed) return;
        if (_runtime != null && !_closedExperience) _preferencesDirty = true;
        FlushPreferences();
        _preferencesDisposed = true;
        if (_preferencesTimer != null) { _preferencesTimer.Stop(); _preferencesTimer.Tick -= PreferencesTimerTick; }
        DetachFeatureParameterObservers();
        foreach (var unsubscribe in _parameterUnsubscribe) unsubscribe();
        _parameterUnsubscribe.Clear();
    }
}
