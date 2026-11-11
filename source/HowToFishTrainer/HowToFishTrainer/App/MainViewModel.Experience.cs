using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class MainViewModel
{
    public CombatFeedbackViewModel CombatFeedback { get; } = new();
    private ExperienceSettings _experience = new();
    private ExperienceSettingsStore _experienceStore = new(null);
    private DispatcherTimer? _experienceSaveTimer;
    private bool _experienceDirty, _presetBusy;
    private bool _closedExperience;
    private string _activePresetName="上次参数";
    public string ActivePresetName { get=>_activePresetName;private set=>Set(ref _activePresetName,value); }
    private string _experienceStatus = "预设只恢复参数，持续功能需手动开启";
    private ParameterPreset? _selectedPreset;
    private string _presetName = "探索", _featureQuery = "";
    private SearchHit? _selectedSearchHit;
    private string _pointNote = "", _pointColor = "青色", _pointIcon = "⌖";
    private bool _pointFavorite;
    private TrainerTeleportPoint? _safePosition;
    private TrainerTeleportPoint? _lastLivingPosition, _deathPosition;
    private bool _trackDeathPosition, _lastSampleAlive;
    private DateTimeOffset _lastDeathSample;
    private bool _deathSampleBusy;
    private bool _positionActionBusy;
    private int _positionContextEpoch;
    private long _positionEditorRevision;
    private bool IsCurrentPositionContext(IGameRuntimeClient runtime, int connection, int context) =>
        runtime == _runtime && connection == _connectionEpoch && context == _positionContextEpoch;
    private void InitializePositionEditorTracking()
    {
        foreach (var input in new[] { CurrentXInput, CurrentYInput, CurrentZInput })
        {
            PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == nameof(NumericInput.Text)) _positionEditorRevision++; };
            input.PropertyChanged += handler;
            _parameterUnsubscribe.Add(() => input.PropertyChanged -= handler);
        }
        string? selectedKey = RoomMembersPage.SelectedMember?.Key;
        PropertyChangedEventHandler memberHandler = (_, e) => {
            if (e.PropertyName != nameof(RoomMembersPage.SelectedMember)) return;
            string? key = RoomMembersPage.SelectedMember?.Key;
            if (key != selectedKey && TargetRoomMember) _positionEditorRevision++;
            selectedKey = key;
            OnPropertyChanged(nameof(SelectedTeleportMember));
        };
        RoomMembersPage.PropertyChanged += memberHandler;
        _parameterUnsubscribe.Add(() => RoomMembersPage.PropertyChanged -= memberHandler);
    }
    public bool CanPositionAction => !_positionActionBusy && !PresetBusy;
    private async Task RunPositionActionAsync(Func<Task> action)
    {
        if(!CanPositionAction)return;
        _positionActionBusy=true;OnPropertyChanged(nameof(CanPositionAction));int epoch=_connectionEpoch,context=_positionContextEpoch;
        try{await action();}
        catch(Exception e){if(epoch==_connectionEpoch&&context==_positionContextEpoch)PositionStatus="位置操作失败 · "+e.Message;}
        finally{if(epoch==_connectionEpoch&&context==_positionContextEpoch){_positionActionBusy=false;OnPropertyChanged(nameof(CanPositionAction));OverlayStatus=PositionStatus;}}
    }
    public bool TrackDeathPosition { get=>_trackDeathPosition; set=>Set(ref _trackDeathPosition,value); }
    private string _deathPositionStatus="开启后每两秒读取本机状态，记录倒地前最后一个采样位置";
    public string DeathPositionStatus { get=>_deathPositionStatus;private set=>Set(ref _deathPositionStatus,value); }
    private string _combatDiagnostics = "连接游戏后读取武器与瞄准数据";
    private bool _diagnosticOnly, _autoRefill, _experienceWorkBusy;
    private int _experienceWorkVersion, _spinStopVersion;
    private int _autoRefillCount, _autoGeneration;
    private string _autoRefillStatus = "自动补充已关闭";
    private DateTimeOffset _lastRefill, _spinStarted;
    private bool _spinWasEnabled;
    private bool _spinStopPending;
    private double? _curveAppliedSpeed;
    private int _pointMetadataRevision;
    public int PointMetadataRevision => _pointMetadataRevision;
    public PointDecoration? GetPointDecoration(TrainerTeleportPoint point)=>_experience.Points.FirstOrDefault(p=>p.Group==point.Group&&p.Name==point.Name);
    private void PointMetadataChanged(){_pointMetadataRevision++;OnPropertyChanged(nameof(PointMetadataRevision));}

    public ObservableCollection<ParameterPreset> Presets { get; } = new();
    public ParameterPreset? SelectedPreset { get => _selectedPreset; set => Set(ref _selectedPreset,value); }
    public string PresetName { get => _presetName; set => Set(ref _presetName,value ?? ""); }
    public string ExperienceStatus { get => _experienceStore.Warning ?? _experienceStatus; private set => Set(ref _experienceStatus,value); }
    public bool PresetBusy { get => _presetBusy; private set { if(Set(ref _presetBusy,value)){OnPropertyChanged(nameof(CanRestoreLastSession));OnPropertyChanged(nameof(CanEditParameters));OnPropertyChanged(nameof(CanApplyEntityOverlay));NotifyGullActions();OnPropertyChanged(nameof(CanPositionAction));OnPropertyChanged(nameof(CanLoadSelectedIsland));foreach(var f in AllFeatures)f.RefreshExperienceAvailability();} } }
    public bool CanEditParameters => !PresetBusy;
    private int _presetApplyVersion;
    private void InvalidatePresetApply()
    {
        Interlocked.Increment(ref _presetApplyVersion);
        PresetBusy=false;
    }
    public bool WidgetVisible { get => _experience.WidgetVisible; set { if (_experience.WidgetVisible == value) return; _experience.WidgetVisible=value; OnPropertyChanged(); ScheduleExperienceSave(); } }
    public double WidgetLeft { get => _experience.WidgetLeft; set { _experience.WidgetLeft=value; ScheduleExperienceSave(); } }
    public double WidgetTop { get => _experience.WidgetTop; set { _experience.WidgetTop=value; ScheduleExperienceSave(); } }
    public bool CompactNavigation { get => _experience.CompactNavigation; set { if (_experience.CompactNavigation == value) return; _experience.CompactNavigation=value; OnPropertyChanged(); ScheduleExperienceSave(); } }
    public string FeatureQuery { get => _featureQuery; set { if (Set(ref _featureQuery,value ?? "")) OnPropertyChanged(nameof(SearchResults)); } }
    public IReadOnlyList<SearchHit> SearchResults => GetSearchResults();
    public SearchHit? SelectedSearchHit
    {
        get => _selectedSearchHit;
        set { if (!Set(ref _selectedSearchHit,value) || value == null) return; var page=Pages.FirstOrDefault(p=>p.Category==value.Category); if(page!=null) SelectedPage=page; ExperienceStatus="已定位分类 · "+value.Title; }
    }
    public IReadOnlyList<FeatureItemViewModel> FeelFeatures { get { EnsureFeatureViews();return _feelView; } }
    public bool HasFeelFeatures => FeelFeatures.Count > 0;
    public IReadOnlyList<FeatureItemViewModel> DisplayFeatures { get { EnsureFeatureViews();return _displayView; } }
    public string ActiveFeaturesText
    {
        get {
            var titles = _toggleStates.Where(e=>e.Value.Enabled).Select(e=>FeatureRegistry.All.FirstOrDefault(f=>f.Id==e.Key)?.Title ?? e.Key).ToList();
            if(AimAssist.Enabled) titles.Add("辅助瞄准"); if(Crosshair.Enabled) titles.Add("外置准星"); if(AutoRefill) titles.Add("海鸥自动补充"); if(CombatFeedback.Active)titles.Add("战斗反馈");
            return titles.Count==0 ? "持续功能均已关闭" : string.Join(" · ",titles);
        }
    }
    public string CombatDiagnostics { get => _combatDiagnostics; private set => Set(ref _combatDiagnostics,value); }
    public bool DiagnosticOnly
    {
        get => _diagnosticOnly;
        set { if(!Set(ref _diagnosticOnly,value))return; if(value) { AimAssist.Enabled=false; var silent=AllFeatures.FirstOrDefault(f=>f.Descriptor.Id=="combat.silent_aim"); if(silent?.Enabled==true)silent.Enabled=false; } foreach(var f in AllFeatures)f.RefreshExperienceAvailability(); }
    }
    public string[] PointColors { get; } = { "青色","蓝色","紫色","金色","绿色" };
    public string[] PointIcons { get; } = { "⌖","★","⚑","⚓","◆" };
    public string[] PointSortModes { get; } = { "收藏优先","名称","分组","最近添加" };
    public string PointNote { get => _pointNote; set => Set(ref _pointNote,value ?? ""); }
    public string PointColor { get => _pointColor; set { if(PointColors.Contains(value))Set(ref _pointColor,value); } }
    public string PointIcon { get => _pointIcon; set { if(PointIcons.Contains(value))Set(ref _pointIcon,value); } }
    public bool PointFavorite { get => _pointFavorite; set => Set(ref _pointFavorite,value); }
    public string PointSort { get => _experience.PointSort; set { if(!PointSortModes.Contains(value))return; _experience.PointSort=value; OnPropertyChanged(); RefreshTeleportFilter(); ScheduleExperienceSave(); } }
    public string[] SpinCurves { get; } = { "固定","缓慢加速","脉冲" };
    public string SpinCurve { get => _experience.SpinCurve; set { if(!SpinCurves.Contains(value))return; _experience.SpinCurve=value; _spinStarted=DateTimeOffset.UtcNow; _curveAppliedSpeed=null; OnPropertyChanged(); ScheduleExperienceSave(); } }
    public NumericInput RefillLimitInput { get; } = new("自动补充波次上限",3,1,20,true);
    public int AutoRefillCount { get => _autoRefillCount; private set => Set(ref _autoRefillCount,value); }
    public string AutoRefillStatus { get => _autoRefillStatus; private set => Set(ref _autoRefillStatus,value); }
    private enum GullRoundState { Idle, Starting, StartUnconfirmed, Active, Stopping, StopUnconfirmed }
    private GullRoundState _gullState;
    private string? _gullRoundId;
    private int _gullRequestedCount;
    private string _gullChallengeStatus = "手持枪械开始，限时 60 秒。使用海鸥迁徙的每波数量，自动补充需先关闭。";
    public string GullChallengeStatus { get => _gullChallengeStatus; private set => Set(ref _gullChallengeStatus,value); }
    public FunEventCardViewModel GullMigration => FunEventsPage.Events.Single(card => card.Id == "seagull-migration");
    private bool CanControlChallenge => !_closedExperience && !PresetBusy && _runtime != null && _capabilities.CanExecute(Capability.HostWorld);
    public bool CanStartGullChallenge => CanControlChallenge && !AutoRefill && GullMigration.CanExecute &&
        _gullState is GullRoundState.Idle or GullRoundState.StartUnconfirmed;
    public bool CanStopGullChallenge => CanControlChallenge && _gullState is GullRoundState.Active or GullRoundState.StartUnconfirmed or GullRoundState.StopUnconfirmed;
    internal string? GullRoundForInspection => _gullState is GullRoundState.Active or GullRoundState.StopUnconfirmed ? _gullRoundId : null;
    public ICommand StartGullChallengeCommand => new RelayCommand(() => _ = RunGullChallengeAsync(true));
    public ICommand StopGullChallengeCommand => new RelayCommand(() => _ = RunGullChallengeAsync(false));

    private void NotifyGullActions()
    {
        OnPropertyChanged(nameof(CanStartGullChallenge));
        OnPropertyChanged(nameof(CanStopGullChallenge));
    }

    private void ResetGullChallenge(string status)
    {
        if (_gullState == GullRoundState.Idle) return;
        _gullRoundId = null;
        _gullState = GullRoundState.Idle;
        GullChallengeStatus = status;
        NotifyGullActions();
    }

    private bool ApplyGullResult(JsonElement envelope)
    {
        if (_gullRoundId == null || envelope.ValueKind != JsonValueKind.Object ||
            !envelope.TryGetProperty("gullChallenge", out var round) || round.ValueKind != JsonValueKind.Object ||
            !round.TryGetProperty("roundId", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != _gullRoundId ||
            !round.TryGetProperty("active",out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !round.TryGetProperty("total",out var total) || total.ValueKind != JsonValueKind.Number || !total.TryGetInt32(out int targets) || targets is < 0 or > 50 || targets == 0 && active.GetBoolean() ||
            !round.TryGetProperty("hits",out var hits) || hits.ValueKind != JsonValueKind.Number || !hits.TryGetInt32(out int hitCount) || hitCount < 0 ||
            !round.TryGetProperty("kills",out var kills) || kills.ValueKind != JsonValueKind.Number || !kills.TryGetInt32(out int killCount) || killCount < 0 || killCount > targets || killCount > hitCount)
            return false;
        string firstHit = "尚未命中";
        if (hitCount > 0)
        {
            if (!round.TryGetProperty("firstHitSeconds",out var first) || first.ValueKind != JsonValueKind.Number ||
                !first.TryGetDouble(out double seconds) || !double.IsFinite(seconds) || seconds < 0) return false;
            firstHit = FormattableString.Invariant($"首中 {seconds:0.00} 秒");
        }
        bool running = active.GetBoolean();
        string label = running ? "进行中" : round.TryGetProperty("reason",out var reason) && reason.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(reason.GetString()) ? reason.GetString()! : "本轮结束";
        var previous = _gullState;
        _gullState = running ? GullRoundState.Active : GullRoundState.Idle;
        if (!running) _gullRoundId = null;
        GullChallengeStatus = targets == 0 ? label : $"{label} · {firstHit} · 有效命中 {hitCount} 次 · 击落 {killCount}/{targets}";
        if (_gullState != previous) NotifyGullActions();
        return true;
    }

    public async Task RunGullChallengeAsync(bool start)
    {
        if (start ? !CanStartGullChallenge : !CanStopGullChallenge) return;
        if (_gullRoundId == null)
        {
            _gullRoundId = Guid.NewGuid().ToString("N");
            _gullRequestedCount = GullMigration.RequestedCount;
        }
        var runtime = _runtime!;
        string roundId = _gullRoundId;
        int epoch = _connectionEpoch, context = _positionContextEpoch;
        bool Current() => !_closedExperience && _gullRoundId == roundId && IsCurrentPositionContext(runtime,epoch,context);
        var previous = _gullState;
        _gullState = start ? GullRoundState.Starting : GullRoundState.Stopping;
        GullChallengeStatus = start ? "等待游戏登记本轮海鸥…" : "正在结束本轮…";
        NotifyGullActions();
        try
        {
            var args = start ? JsonSerializer.SerializeToElement(new { roundId, count = _gullRequestedCount })
                             : JsonSerializer.SerializeToElement(new { roundId });
            var result = await runtime.ExecuteAsync(new FeatureCommand(start ? "fun.challenge.start" : "fun.challenge.stop",
                args,Capability.HostWorld),CancellationToken.None);
            if (!Current()) return;
            if (result.Succeeded)
            {
                if (result.Applied is JsonElement data && data.ValueKind == JsonValueKind.Object)
                {
                    if (ApplyGullResult(data)) return;
                    if (!start && data.TryGetProperty("roundId",out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == roundId &&
                        data.TryGetProperty("missing",out var missing) && missing.ValueKind == JsonValueKind.True)
                    {
                        ResetGullChallenge("本轮已不存在，最终成绩未知");
                        return;
                    }
                }
                throw new InvalidDataException("游戏未返回本轮成绩");
            }
            if (start && previous == GullRoundState.Idle) ResetGullChallenge(result.Message);
            else { _gullState = previous; GullChallengeStatus = result.Message; NotifyGullActions(); }
        }
        catch (Exception error)
        {
            if (!Current()) return;
            _gullState = start ? GullRoundState.StartUnconfirmed : GullRoundState.StopUnconfirmed;
            GullChallengeStatus = "本轮状态待确认，可重试 · " + error.Message;
            NotifyGullActions();
        }
    }

    public bool AutoRefill
    {
        get => _autoRefill;
        set {
            if(value && _gullState != GullRoundState.Idle){AutoRefillStatus="先结束当前飞靶挑战";OnPropertyChanged();return;}
            if(value && (_runtime==null || !_capabilities.CanExecute(Capability.HostWorld) || RefillLimitInput.HasError)) { AutoRefillStatus="进入世界并填写 1–20 的整数上限后再开启"; OnPropertyChanged(); return; }
            if(!Set(ref _autoRefill,value))return;
            NotifyGullActions();
            _autoGeneration++; if(value)AutoRefillCount=0; _lastRefill=DateTimeOffset.MinValue;
            AutoRefillStatus=value?"等待海鸥存活数量；耗尽后按海鸥迁徙的数量设置补充":"自动补充已停止";
            OnPropertyChanged(nameof(ActiveFeaturesText));
        }
    }

    public ICommand SavePresetCommand => new RelayCommand(SavePreset);
    public ICommand ApplyPresetCommand => new RelayCommand(()=>_ = ApplySelectedPresetAsync());
    public ICommand DeletePresetCommand => new RelayCommand(()=> { if(SelectedPreset==null||PresetBusy)return; RemoveOverlayReference(new("preset",SelectedPreset.Name)); _experience.Presets.Remove(SelectedPreset); Presets.Remove(SelectedPreset); SelectedPreset=Presets.FirstOrDefault(); ScheduleExperienceSave(); });
    public ICommand SavePointDetailsCommand => new RelayCommand(()=> {
        var point=SelectedTeleportPoint; if(point==null) { PositionStatus="请先选择已保存的坐标"; return; }
        if(PointNote.Length>500) { PositionStatus="备注最多 500 字";return; }
        _experience.Points.RemoveAll(p=>p.Group==point.Group&&p.Name==point.Name);
        _experience.Points.Add(new(point.Group,point.Name,PointColor,PointIcon,PointNote,PointFavorite)); ScheduleExperienceSave(); PointMetadataChanged(); RefreshTeleportFilter(); PositionStatus="标记、备注与收藏已保存";
    });
    public ICommand RememberSafePositionCommand => new RelayCommand(()=>_ = RememberSafePositionAsync());
    public ICommand SelectSafePositionCommand => new RelayCommand(()=> {
        if(_safePosition==null){PositionStatus="当前世界尚未记录安全位置";return;}
        TargetRoomMember=false; SelectedTeleportPoint=null; CurrentX=_safePosition.X; CurrentY=_safePosition.Y; CurrentZ=_safePosition.Z; PositionStatus="安全位置已填入，点击传送执行";
    });
    public ICommand SelectDeathPositionCommand => new RelayCommand(()=> {
        if(_deathPosition==null){PositionStatus="当前世界尚未记录死亡前位置";return;}
        TargetRoomMember=false;SelectedTeleportPoint=null;CurrentX=_deathPosition.X;CurrentY=_deathPosition.Y;CurrentZ=_deathPosition.Z;PositionStatus="倒地前最后一个采样位置已填入，点击传送执行";
    });
    public ICommand StopFunCommand => new RelayCommand(()=>{AutoRefill=false; if(_toggleStates.TryGetValue("player.spin.lock",out var state)&&state.Enabled) _=StopSpinAsync();});

    private void InitializeExperience(string? path)
    {
        _experienceStore=new(path); _experience=_experienceStore.Load();
        CombatFeedback.Load(_experience.CombatFeedback);
        CombatFeedback.SettingsChanged+=()=>{_experience.CombatFeedback=CombatFeedback.Capture();ScheduleExperienceSave();};
        CombatFeedback.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(CombatFeedback.Active))OnPropertyChanged(nameof(ActiveFeaturesText));};
        foreach(var card in FunEventsPage.Events.Where(e=>e.HasAnimalCount))
        {
            if(_experience.AnimalCounts.TryGetValue(card.Id,out int count))card.CountInput.Value=count;
            card.CountInput.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NumericInput.Text)){_experience.AnimalCounts[card.Id]=card.RequestedCount;ScheduleExperienceSave();}};
        }
        foreach(var preset in _experience.Presets)Presets.Add(preset); SelectedPreset=Presets.FirstOrDefault();
        RefillLimitInput.Value=_experience.RefillLimit;
        RefillLimitInput.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NumericInput.Text)){_experience.RefillLimit=(int)RefillLimitInput.LastValidValue; ScheduleExperienceSave();}};
        _experienceSaveTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(500)};
        _experienceSaveTimer.Tick+=SaveExperienceTick;
        GullMigration.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GullMigration.CanExecute)) NotifyGullActions(); };
        FunEventsPage.CleanupRequested+=()=>AutoRefill=false;
        AimAssist.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(AimAssist.Enabled)){if(DiagnosticOnly&&AimAssist.Enabled)AimAssist.Enabled=false; OnPropertyChanged(nameof(ActiveFeaturesText));}};
        Crosshair.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(Crosshair.Enabled))OnPropertyChanged(nameof(ActiveFeaturesText));};
        foreach(var feature in AllFeatures)AttachExperience(feature);
    }
    private void AttachExperience(FeatureItemViewModel feature)
    {
        int actionEpoch=_featureActionEpoch;
        feature.AttachExperience(_experience,ScheduleExperienceSave,(f,parameters)=> {
        if(_runtime==null||actionEpoch!=_featureActionEpoch)return;
        var option=f.Options.FirstOrDefault(o=>o.Id==parameters.Number("optionId"));if(option==null)return;
        _experience.Recent.RemoveAll(e=>e.FeatureId==f.Descriptor.Id&&e.Id==option.Id);
        _experience.Recent.Insert(0,new(f.Descriptor.Id,option.Id,option.Name,parameters.Copy()));
        if(_experience.Recent.Count>30)_experience.Recent.RemoveRange(30,_experience.Recent.Count-30);
        ScheduleExperienceSave();
    },()=>actionEpoch!=_featureActionEpoch?"参数实例已过期，请使用当前页面":PresetBusy?"预设切换中，请稍候":DiagnosticOnly&&feature.Descriptor.Id=="combat.silent_aim"?"观察诊断模式已暂停魔法子弹":null);
    }
    private void ScheduleExperienceSave(){if(_closedExperience)return;_experienceDirty=true;_experienceSaveTimer?.Stop();_experienceSaveTimer?.Start();}
    private void SaveExperienceTick(object? sender,EventArgs args)=>FlushExperience();
    public bool FlushExperience()
    {
        _experienceSaveTimer?.Stop(); if(!_experienceDirty)return true;
        try{_experienceStore.Save(_experience);_experienceDirty=false;return true;}
        catch(Exception){return false;}
        finally{OnPropertyChanged(nameof(ExperienceStatus));OnPropertyChanged(nameof(OverlayStatus));}
    }
    public void CloseExperience(){if(_closedExperience)return;if(_runtime!=null){_preferencesDirty=true;FlushPreferences();}InvalidatePresetApply();StopExperienceAutomation();FlushExperience();_closedExperience=true;OnPropertyChanged(nameof(CanRestoreLastSession)); if(_experienceSaveTimer!=null){_experienceSaveTimer.Stop();_experienceSaveTimer.Tick-=SaveExperienceTick;}}
    private void SavePreset()
    {
        string name=PresetName.Trim(); if(name.Length==0||name.Length>40){ExperienceStatus="预设名称需要 1–40 个字符";return;}
        if(PresetBusy)return;
        var old=_experience.Presets.FirstOrDefault(p=>string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase));
        if(old==null&&Presets.Count>=50){ExperienceStatus="最多保存 50 套预设";return;}
        var preset=new ParameterPreset(name,CapturePreferences().ToJson());
        if(old!=null){ReplaceOverlayReference(new("preset",old.Name),new("preset",name));_experience.Presets.Remove(old);Presets.Remove(old);}
        _experience.Presets.Add(preset);Presets.Add(preset);SelectedPreset=preset;ActivePresetName=name;ScheduleExperienceSave();ExperienceStatus="预设已保存 · "+name;
    }
    public Task ApplySelectedPresetAsync() => ApplySavedPreferencesAsync(SelectedPreset?.Parameters, SelectedPreset?.Name ?? "", false);
    private async Task ApplySavedPreferencesAsync(string? json, string name, bool restoreSession)
    {
        if(json==null||PresetBusy||_closedExperience)return;
        int version=Interlocked.Increment(ref _presetApplyVersion);
        var runtime=_runtime;int epoch=_connectionEpoch,context=_positionContextEpoch;
        bool Current()=>version==Volatile.Read(ref _presetApplyVersion)&&!_closedExperience&&runtime==_runtime&&epoch==_connectionEpoch&&context==_positionContextEpoch;
        PresetBusy=true;
        try{
            if(!Current())return;
            var parameters=TrainerUserPreferences.Parse(json);
            InvalidateFeatureActions();
            epoch=Interlocked.Increment(ref _connectionEpoch);
            ResetPendingSessionWork();
            context=_positionContextEpoch;
            // Stop accepting aim requests before clearing all locks; no old intent may replay after clear.
            StopExperienceAutomation(); AimAssist.Disconnect();
            try
            {
                if(runtime!=null){var result=await runtime.ExecuteAsync(new FeatureCommand("locks.disable_all",JsonSerializer.SerializeToElement(new{}),Capability.OwnedPlayer),CancellationToken.None);if(!Current())return;if(!result.Succeeded){ExperienceStatus="未切换预设 · 停用失败，请检查游戏中的持续效果："+result.Message;return;}}
                MarkAllLocksDisabled("预设切换，已停用"); AimAssist.Disconnect(); RoomMembersPage.Disconnect();
                if(runtime!=null)RoomMembersPage.Connect(runtime,_capabilities);
                _restoringPreferences=true;
                try{_parameterDrafts.Clear();_preferences=parameters;ApplyPreferences(parameters);}
                finally{_restoringPreferences=false;}
                _experience.CombatFeedback=CombatFeedback.Capture();ScheduleExperienceSave();
            }
            finally { if(Current() && runtime!=null)AimAssist.Connect(runtime,_capabilities); }
            _preferredIslandKey=parameters.Teleport.Text("islandKey");
            SelectedIsland=IslandOptions.FirstOrDefault(i=>i.Key==_preferredIslandKey);
            if(restoreSession && runtime!=null)
            {
                var failures = await RestoreSessionSwitchesAsync(parameters, runtime, Current);
                if(!Current())return;
                ExperienceStatus=failures.Count==0?"已恢复上次会话的参数与功能开关":"上次会话未全部恢复 · "+string.Join("；",failures);
            }
            else ExperienceStatus="已恢复「"+name+"」参数 · 持续功能请手动开启";
            SchedulePreferencesSave();ActivePresetName=name;
        }catch(Exception e){if(Current())ExperienceStatus="预设恢复失败 · "+e.Message;}
        finally{if(Current()){PresetBusy=false;RebuildFeatureModels();OnPropertyChanged(nameof(ActiveFeaturesText));if(_runtime!=null&&IslandOptions.Count==0)_=LoadIslandCatalogAsync(_runtime,_connectionEpoch);}}
    }
    public async Task EmergencyDisableAsync(IGameRuntimeClient? commandRuntime = null)
    {
        if(_closedExperience)return;
        InvalidatePresetApply();
        CaptureSessionSwitches();
        foreach(var descriptor in FeatureRegistry.All.Where(f=>f.Control==FeatureControl.Toggle))
            _preferences.Session.Put("toggle:"+descriptor.Id,false);
        foreach(string key in new[]{"aimAssist","crosshair","combatFeedback","slotMachine","roulette","autoRefill"})
            _preferences.Session.Put(key,false);
        _preferencesDirty=true;
        int version=Volatile.Read(ref _presetApplyVersion);
        InvalidateFeatureActions();
        int epoch=Interlocked.Increment(ref _connectionEpoch);
        ResetPendingSessionWork();
        var runtime=_runtime;
        commandRuntime??=runtime;
        int context=_positionContextEpoch;
        bool Current()=>version==Volatile.Read(ref _presetApplyVersion)&&!_closedExperience&&runtime==_runtime&&epoch==_connectionEpoch&&context==_positionContextEpoch;
        PresetBusy=true;
        StopExperienceAutomation(); AimAssist.Disconnect(); Crosshair.Enabled=false;
        foreach(var pair in _toggleStates.Where(p=>p.Value.Enabled).ToArray())
            ApplyRememberedToggleState(pair.Key,pair.Value with {Enabled=false,Status="停用请求待确认",StateLabel="等待确认",IsSuccessful=false});
        try
        {
            if(commandRuntime==null){MarkAllLocksDisabled("已停用本地持续功能");return;}
            var result=await commandRuntime.ExecuteAsync(new FeatureCommand("locks.disable_all",JsonSerializer.SerializeToElement(new{}),Capability.OwnedPlayer),CancellationToken.None);
            if(!Current())return;
            ModeStatus=result.Succeeded?"已紧急停用全部持续修改":"紧急停用失败 · "+result.Message;
            if(result.Succeeded)MarkAllLocksDisabled("已通过紧急快捷键停用");
        }
        catch(Exception e){if(Current())ModeStatus="紧急停用失败 · "+e.Message;}
        finally
        {
            if(Current())
            {
                if(runtime!=null)AimAssist.Connect(runtime,_capabilities);
                PresetBusy=false;RebuildFeatureModels();
                if(runtime!=null&&IslandOptions.Count==0)_=LoadIslandCatalogAsync(runtime,_connectionEpoch);
            }
        }
    }
    private void LoadPointDetails()
    {
        var point=SelectedTeleportPoint;var meta=_experience.Points.FirstOrDefault(p=>p.Group==point?.Group&&p.Name==point?.Name);
        PointNote=meta?.Note??"";PointColor=meta!=null&&PointColors.Contains(meta.Color)?meta.Color:"青色";
        PointIcon=meta!=null&&PointIcons.Contains(meta.Icon)?meta.Icon:"⌖";PointFavorite=meta?.Favorite??false;
    }
    private Dictionary<(string Group,string Name),(PointDecoration First,bool Favorite)> CreatePointMetadataIndex()
    {
        // Exact-case metadata keys and the first annotation match the previous lookup.
        // Favorite sorting used Any(), so retain a later duplicate's favorite flag too.
        var index=new Dictionary<(string Group,string Name),(PointDecoration First,bool Favorite)>();
        foreach(var point in _experience.Points)
        {
            var key=(point.Group,point.Name);
            if(index.TryGetValue(key,out var old))
            {if(point.Favorite&&!old.Favorite)index[key]=(old.First,true);}
            else index.Add(key,(point,point.Favorite));
        }
        return index;
    }
    private IEnumerable<TrainerTeleportPoint> GetFilteredTeleportPoints(string query)
    {
        // Rebuild for each operation; edits/imports and culture changes cannot leave a stale cache.
        var metadata=CreatePointMetadataIndex();string group=SelectedTeleportGroup;
        var points=TeleportPoints.Where(p=>(group=="全部分组"||string.Equals(p.Group,group,StringComparison.CurrentCultureIgnoreCase))&&
            (query.Length==0||(p.Name+" "+p.Group+" "+(metadata.TryGetValue((p.Group,p.Name),out var item)?item.First.Note:null)).Contains(query,StringComparison.CurrentCultureIgnoreCase)));
        return PointSort switch{
            "名称"=>points.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase),
            "分组"=>points.OrderBy(p=>p.Group,StringComparer.CurrentCultureIgnoreCase).ThenBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase),
            "最近添加"=>points.Reverse(),
            _=>points.OrderByDescending(p=>metadata.TryGetValue((p.Group,p.Name),out var item)&&item.Favorite).ThenBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase)
        };
    }
    public string ExportTeleportPoints()
    {
        var metadata=CreatePointMetadataIndex();
        return JsonSerializer.Serialize(new {version=1,points=TeleportPoints.Select(p=>new{group=p.Group,name=p.Name,x=p.X,y=p.Y,z=p.Z,decoration=metadata.TryGetValue((p.Group,p.Name),out var item)?item.First:null})},new JsonSerializerOptions{WriteIndented=true});
    }
    public int ImportTeleportPoints(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(!root.TryGetProperty("version",out var v)||!v.TryGetInt32(out int version)||version!=1||!root.TryGetProperty("points",out var entries)||entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>2000)throw new JsonException("传送点格式或版本无效（最多 2000 个）");
        var staged=TeleportPoints.ToList();var decorations=_experience.Points.ToList();int imported=0;
        var names=new Dictionary<string,HashSet<string>>(StringComparer.CurrentCultureIgnoreCase);
        bool RememberName(string group,string name)
        {
            if(!names.TryGetValue(group,out var set))names[group]=set=new(StringComparer.CurrentCultureIgnoreCase);
            return set.Add(name);
        }
        foreach(var point in staged)RememberName(point.Group,point.Name);
        foreach(var e in entries.EnumerateArray()){
            string? group=e.TryGetProperty("group",out var g)&&g.ValueKind==JsonValueKind.String?g.GetString():null;
            string? name=e.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString():null;
            double Coordinate(string key){if(!e.TryGetProperty(key,out var c)||c.ValueKind!=JsonValueKind.Number||!c.TryGetDouble(out double d)||!double.IsFinite(d)||Math.Abs(d)>100000)throw new JsonException("坐标超出有效范围");return d;}
            if(string.IsNullOrWhiteSpace(group)||string.IsNullOrWhiteSpace(name)||group.Length>80||name.Length>80)throw new JsonException("传送点名称或分组无效");
            double x=Coordinate("x"),y=Coordinate("y"),z=Coordinate("z");
            // Merge only; a same-name point is never silently overwritten.
            if(!RememberName(group,name))continue;
            if(staged.Count>=2000)throw new JsonException("合并后的传送点超过 2000 个");
            staged.Add(new(group,name,x,y,z));imported++;
            if(e.TryGetProperty("decoration",out var meta)&&meta.ValueKind==JsonValueKind.Object){var d=JsonSerializer.Deserialize<PointDecoration>(meta.GetRawText());if(d!=null&&d.Note?.Length<=500&&PointColors.Contains(d.Color)&&PointIcons.Contains(d.Icon))decorations.Add(d with{Group=group,Name=name});}
        }
        _settings.Save(staged);foreach(var p in staged.Skip(staged.Count-imported))TeleportPoints.Add(p);_experience.Points=decorations;ScheduleExperienceSave();PointMetadataChanged();RefreshTeleportGroups();RefreshTeleportFilter();PositionStatus=$"已导入 {imported} 个新坐标，同名坐标保留原值";return imported;
    }
    private async Task RememberSafePositionAsync()
    {
        var runtime=_runtime;int epoch=_connectionEpoch,context=_positionContextEpoch;if(runtime==null){PositionStatus="请先连接游戏";return;}
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("world.position.read",JsonSerializer.SerializeToElement(new{}),Capability.ReadOnly),CancellationToken.None);if(!IsCurrentPositionContext(runtime,epoch,context))return;
            if(result.Succeeded&&result.Applied is JsonElement p&&p.ValueKind==JsonValueKind.Object){double x=p.GetProperty("x").GetDouble(),y=p.GetProperty("y").GetDouble(),z=p.GetProperty("z").GetDouble();if(!new[]{x,y,z}.All(d=>double.IsFinite(d)&&Math.Abs(d)<=100000))throw new InvalidDataException("位置无效");_safePosition=new("临时","本次安全位置",x,y,z);PositionStatus="本机安全位置已记录，仅在当前世界有效";}else PositionStatus=result.Message;
        }catch(Exception e){if(IsCurrentPositionContext(runtime,epoch,context))PositionStatus="记录安全位置失败 · "+e.Message;}
    }
    internal void StopExperienceAutomation()
    {
        if (_gullState is GullRoundState.Starting or GullRoundState.Stopping)
        {
            _gullState = _gullState == GullRoundState.Starting ? GullRoundState.StartUnconfirmed : GullRoundState.StopUnconfirmed;
            GullChallengeStatus = "等待游戏确认飞靶挑战状态";
            NotifyGullActions();
        }
        FunEventsPage.SlotMachine.CancelPendingOperation();
        AutoRefill = false;
        CombatFeedback.ResetSession();
        _autoGeneration++;
        _experienceWorkVersion++;
        _experienceWorkBusy = false;
        _spinStopVersion++;
        _spinStopPending = false;
        _spinWasEnabled = false;
        _curveAppliedSpeed = null;
    }
    private void ResetPendingSessionWork(){_positionContextEpoch++;_islandLoadBusy=false;Volatile.Write(ref _proofRefreshBusy,0);_positionActionBusy=false;_deathSampleBusy=false;OnPropertyChanged(nameof(CanPositionAction));OnPropertyChanged(nameof(CanLoadSelectedIsland));}
    internal void ResetExperienceWorld(){ResetGullChallenge("已离开本轮飞靶挑战");InvalidatePresetApply();StopExperienceAutomation();CombatFeedback.ResetSession();ResetPendingSessionWork();_safePosition=null;_lastLivingPosition=_deathPosition=null;_lastSampleAlive=false;TrackDeathPosition=false;DeathPositionStatus="世界已切换，临时位置已清空";CombatDiagnostics="世界已切换，等待读取新会话";}
    public void UpdateExperienceSession(JsonElement? raw) => UpdateExperienceSession(raw, null);
    public void UpdateExperienceSession(JsonElement? raw, string? inspectedGullRound)
    {
        if(raw is JsonElement feedbackSession)
        {
            CombatFeedback.UpdateSession(feedbackSession);
            if (_gullState is GullRoundState.Active or GullRoundState.StartUnconfirmed or GullRoundState.StopUnconfirmed)
            {
                if (inspectedGullRound != null && inspectedGullRound == GullRoundForInspection &&
                    feedbackSession.ValueKind == JsonValueKind.Object && feedbackSession.TryGetProperty("gullChallenge",out var round) && round.ValueKind == JsonValueKind.Null)
                    ResetGullChallenge("本轮已从运行时清除，最终成绩未知");
                else ApplyGullResult(feedbackSession);
            }
        }
        if(raw is not JsonElement session||session.ValueKind!=JsonValueKind.Object){CombatDiagnostics="当前运行时未提供诊断数据";if(AutoRefill){AutoRefill=false;AutoRefillStatus="缺少会话计数，自动补充已停止";}return;}
        if(!session.TryGetProperty("predictiveAim",out var aim)||aim.ValueKind!=JsonValueKind.Object){CombatDiagnostics="当前运行时未提供瞄准诊断";if(AutoRefill){AutoRefill=false;AutoRefillStatus="缺少迁徙海鸥计数，自动补充已停止";}return;}
        static JsonElement Field(JsonElement p,string key)=>p.ValueKind==JsonValueKind.Object&&p.TryGetProperty(key,out var v)?v:default;
        static string TextValue(JsonElement p,string key,string fallback="—") {var v=Field(p,key);return v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;}
        static string Num(JsonElement p,string key,string format="0.###"){var v=Field(p,key);return v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)?n.ToString(format,CultureInfo.InvariantCulture):"—";}
        var last=Field(aim,"last");var currentWeapon=Field(session,"experienceTelemetry");var weapon=currentWeapon.ValueKind==JsonValueKind.Object?currentWeapon:Field(aim,"weapon");var perf=Field(aim,"performance");var lead=Field(last,"leadOffset");
        string state=TextValue(last,"state");string label=state switch{"off"=>"未启用","waiting-for-ads"=>"等待开镜","predicted"=>"已锁定 · 提前量跟踪","no-target" or "no-visible-target"=>"范围内无可见目标","no-weapon"=>"未装备可用枪械","blocked" or "input-blocked"=>"界面操作/倒地/装备不可用时暂停",_=>state};
        string weaponName=TextValue(weapon,"name",TextValue(weapon,"reason")=="no-weapon"?"未装备枪械":"—");
        CombatDiagnostics=$"状态：{label}    目标：{TextValue(aim,"lockedTarget")}\n当前武器：{weaponName}    弹速：{Num(weapon,"speed")} m/s\n最近跟踪距离：{Num(last,"distance")} m    预计飞行：{Num(last,"flightTime")} s\n提前量 XYZ：{Num(lead,"x")}, {Num(lead,"y")}, {Num(lead,"z")}\n跟踪平均：{Num(perf,"trackingMeanUs")} μs    P95 ≤ {Num(perf,"trackingP95UpperUs")} μs    异常：{Num(perf,"exceptions","0")}\n更新：{DateTime.Now:HH:mm:ss}（复用每秒会话读取；跟踪数据仅在辅助运行时更新）";
        if(AutoRefill){var waves=Field(aim,"seagullWaves");if(waves.ValueKind!=JsonValueKind.Array){AutoRefill=false;AutoRefillStatus="缺少海鸥存活数据，自动补充已停止";}else{
            int alive=0;bool valid=true;foreach(var wave in waves.EnumerateArray()){var count=Field(wave,"alive");if(count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out int n)||n<0||n>100000){valid=false;break;}alive+=n;}
            if(valid)
            {
                var migration = GullMigration;
                string error = NumericInput.FirstError(RefillLimitInput, migration.CountInput);
                if (error.Length != 0) AutoRefillStatus = "自动补充暂停 · " + error;
                else
                {
                    AutoRefillStatus=$"存活 {alive} 只 · 已补充 {AutoRefillCount}/{RefillLimitInput.LastValidValue:0} 波";
                    if (alive == 0) _ = RefillSeagullsAsync(migration);
                }
            }
            else{AutoRefill=false;AutoRefillStatus="海鸥数量数据无效，自动补充已停止";}
        }}
        OnPropertyChanged(nameof(ActiveFeaturesText));
    }
    private async Task RefillSeagullsAsync(FunEventCardViewModel migration)
    {
        if(!AutoRefill||_closedExperience||_experienceWorkBusy||DateTimeOffset.UtcNow-_lastRefill<TimeSpan.FromSeconds(3))return;
        if(AutoRefillCount>=RefillLimitInput.LastValidValue){AutoRefill=false;AutoRefillStatus="已达到补充上限，请手动重新开启";return;}
        var runtime=_runtime;int epoch=_connectionEpoch,generation=_autoGeneration;
        if(runtime==null||!migration.CanExecute||!FunEventsPage.CanExecuteWave)return;
        int work=++_experienceWorkVersion;_experienceWorkBusy=true;_lastRefill=DateTimeOffset.UtcNow;
        try{var result=await FunEventsPage.ExecuteWaveAsync(migration.Id);if(runtime!=_runtime||epoch!=_connectionEpoch||generation!=_autoGeneration)return;
            if(result){AutoRefillCount++;AutoRefillStatus=$"已补充第 {AutoRefillCount} 波（按本波实际数量生成）";}
            else{AutoRefill=false;AutoRefillStatus="补充未成功，已停止；请检查趣味事件结果";}
        }catch(Exception e){if(runtime==_runtime&&epoch==_connectionEpoch){AutoRefill=false;AutoRefillStatus="自动补充失败 · "+e.Message;}}
        finally{if(work==_experienceWorkVersion)_experienceWorkBusy=false;}
    }
    public async Task TickExperienceAsync(DateTimeOffset now)
    {
        if(_closedExperience)return;
        if(TrackDeathPosition && !_deathSampleBusy && now-_lastDeathSample>=TimeSpan.FromSeconds(2) && !PresetBusy)
            await SampleDeathPositionAsync(now);
        bool enabled=_toggleStates.TryGetValue("player.spin.lock",out var state)&&state.Enabled;
        if(!enabled){_spinWasEnabled=false;_curveAppliedSpeed=null;return;}
        if(!_spinWasEnabled){_spinWasEnabled=true;_spinStarted=now;}
        if(_experienceWorkBusy||_runtime==null||PresetBusy||_spinStopPending||_closedExperience)return;
        var live=AllFeatures.FirstOrDefault(f=>f.IsSpin);if(live?.IsBusy==true)return;
        double speed=live?.LastValidSpinSpeed??(_preferences.Features.TryGetValue("player.spin.lock",out var p)?p.Number("spinSpeed")??4:4);
        double desired=SpinCurveValue(SpinCurve,speed,(now-_spinStarted).TotalSeconds);
        if(SpinCurve=="固定"&&_curveAppliedSpeed==null||_curveAppliedSpeed is double prior&&Math.Abs(prior-desired)<0.03)return;
        var runtime=_runtime;int epoch=_connectionEpoch,work=++_experienceWorkVersion;_experienceWorkBusy=true;
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("player.spin.lock",FeatureItemViewModel.SpinArguments(true,desired,live?.CaptureToggleState().AppliedSpinAutoJump??state?.AppliedSpinAutoJump??false),Capability.OwnedPlayer),CancellationToken.None);
            if(runtime!=_runtime||epoch!=_connectionEpoch||work!=_experienceWorkVersion)return;
            if(result.Succeeded)_curveAppliedSpeed=desired;else{SpinCurve="固定";ExperienceStatus="旋转曲线暂停 · "+result.Message;}
        }catch(Exception e){if(runtime==_runtime&&epoch==_connectionEpoch&&work==_experienceWorkVersion)ExperienceStatus="旋转曲线失败 · "+e.Message;}
        finally{if(work==_experienceWorkVersion)_experienceWorkBusy=false;}
    }
    private async Task SampleDeathPositionAsync(DateTimeOffset now)
    {
        var runtime=_runtime;int epoch=_connectionEpoch,context=_positionContextEpoch;if(runtime==null||!_capabilities.CanExecute(Capability.HostWorld))return;
        _deathSampleBusy=true;_lastDeathSample=now;
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("players.catalog",JsonSerializer.SerializeToElement(new{}),Capability.HostWorld),CancellationToken.None);
            if(!IsCurrentPositionContext(runtime,epoch,context)||!TrackDeathPosition)return;
            if(!result.Succeeded||result.Applied is not JsonElement p){DeathPositionStatus="本机状态读取失败";return;}
            ApplyDeathPositionSample(p);
        }catch(Exception e){if(IsCurrentPositionContext(runtime,epoch,context))DeathPositionStatus="本机状态读取失败 · "+e.Message;}
        finally{if(IsCurrentPositionContext(runtime,epoch,context))_deathSampleBusy=false;}
    }
    public void ApplyDeathPositionSample(JsonElement catalog)
    {
        if(!TrackDeathPosition||catalog.ValueKind!=JsonValueKind.Object||!catalog.TryGetProperty("players",out var players)||players.ValueKind!=JsonValueKind.Array)return;
        foreach(var player in players.EnumerateArray()){
            if(player.ValueKind!=JsonValueKind.Object||!player.TryGetProperty("isLocal",out var local)||local.ValueKind!=JsonValueKind.True||!player.TryGetProperty("alive",out var alive)||alive.ValueKind is not (JsonValueKind.True or JsonValueKind.False))continue;
            if(alive.GetBoolean()){
                double Coordinate(string key)=>player.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)&&Math.Abs(n)<=100000?n:double.NaN;
                double x=Coordinate("x"),y=Coordinate("y"),z=Coordinate("z");
                if(!new[]{x,y,z}.All(double.IsFinite))return;
                _lastLivingPosition=new("临时","死亡前位置",x,y,z);_lastSampleAlive=true;
                DeathPositionStatus=_deathPosition==null?"正在记录本机存活位置（每两秒）":"已记录死亡前位置；正在继续观察本机";
            }else{
                if(_lastSampleAlive&&_lastLivingPosition!=null){_deathPosition=_lastLivingPosition;DeathPositionStatus=$"已记录倒地前位置：{_deathPosition.X:0.##}, {_deathPosition.Y:0.##}, {_deathPosition.Z:0.##}";}
                _lastSampleAlive=false;
            }
            break;
        }
    }
    public static double SpinCurveValue(string curve,double speed,double seconds)
    {
        speed=Math.Clamp(double.IsFinite(speed)?speed:4,2,15);seconds=double.IsFinite(seconds)?Math.Max(0,seconds):0;
        return curve switch{"缓慢加速"=>2+(speed-2)*Math.Min(1,seconds/10),"脉冲"=>2+(speed-2)*(.5-.5*Math.Cos(seconds*Math.PI/2)),_=>speed};
    }
    private async Task StopSpinAsync()
    {
        var runtime=_runtime;int epoch=_connectionEpoch;if(runtime==null)return;
        int stop=++_spinStopVersion;_spinStopPending=true;
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("player.spin.lock",FeatureItemViewModel.SpinArguments(false,4),Capability.OwnedPlayer),CancellationToken.None);
            if(runtime==_runtime&&epoch==_connectionEpoch&&stop==_spinStopVersion&&result.Succeeded)ApplyRememberedToggleState("player.spin.lock",new(false,result.Message,"已关闭",true));
        }catch(Exception e){if(runtime==_runtime&&epoch==_connectionEpoch&&stop==_spinStopVersion)ExperienceStatus="停止旋转失败 · "+e.Message;}
        finally{if(stop==_spinStopVersion)_spinStopPending=false;}
    }
}
