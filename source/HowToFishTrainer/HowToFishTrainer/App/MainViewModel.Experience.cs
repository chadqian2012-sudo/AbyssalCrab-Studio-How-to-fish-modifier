using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public bool CanPositionAction => !_positionActionBusy && !PresetBusy;
    private async Task RunPositionActionAsync(Func<Task> action)
    {
        if(!CanPositionAction)return;
        _positionActionBusy=true;OnPropertyChanged(nameof(CanPositionAction));int epoch=_connectionEpoch;
        try{await action();}
        catch(Exception e){if(epoch==_connectionEpoch)PositionStatus="位置操作失败 · "+e.Message;}
        finally{_positionActionBusy=false;OnPropertyChanged(nameof(CanPositionAction));}
    }
    public bool TrackDeathPosition { get=>_trackDeathPosition; set=>Set(ref _trackDeathPosition,value); }
    private string _deathPositionStatus="开启后每两秒读取本机状态，记录倒地前最后一个采样位置";
    public string DeathPositionStatus { get=>_deathPositionStatus;private set=>Set(ref _deathPositionStatus,value); }
    private string _combatDiagnostics = "连接游戏后读取武器与瞄准数据";
    private bool _diagnosticOnly, _autoRefill, _experienceWorkBusy;
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
    public string ExperienceStatus { get => _experienceStatus; private set => Set(ref _experienceStatus,value); }
    public bool PresetBusy { get => _presetBusy; private set { if(Set(ref _presetBusy,value)){OnPropertyChanged(nameof(CanEditParameters));foreach(var f in Features)f.RefreshExperienceAvailability();} } }
    public bool CanEditParameters => !PresetBusy;
    public bool WidgetVisible { get => _experience.WidgetVisible; set { if (_experience.WidgetVisible == value) return; _experience.WidgetVisible=value; OnPropertyChanged(); ScheduleExperienceSave(); } }
    public double WidgetLeft { get => _experience.WidgetLeft; set { _experience.WidgetLeft=value; ScheduleExperienceSave(); } }
    public double WidgetTop { get => _experience.WidgetTop; set { _experience.WidgetTop=value; ScheduleExperienceSave(); } }
    public bool CompactNavigation { get => _experience.CompactNavigation; set { if (_experience.CompactNavigation == value) return; _experience.CompactNavigation=value; OnPropertyChanged(); ScheduleExperienceSave(); } }
    public string FeatureQuery { get => _featureQuery; set { if (Set(ref _featureQuery,value ?? "")) OnPropertyChanged(nameof(SearchResults)); } }
    public IReadOnlyList<SearchHit> SearchResults => string.IsNullOrWhiteSpace(FeatureQuery) ? Array.Empty<SearchHit>() :
        FeatureRegistry.All.Where(f => (f.Title+" "+f.Description+" "+f.Id+" "+DisplayCategory(f.Id,f.Category)).Contains(FeatureQuery.Trim(),StringComparison.CurrentCultureIgnoreCase))
        .Select(f => new SearchHit(f.Title,DisplayCategory(f.Id,f.Category),f.Id,f.Description)).Take(55).ToArray();
    public SearchHit? SelectedSearchHit
    {
        get => _selectedSearchHit;
        set { if (!Set(ref _selectedSearchHit,value) || value == null) return; var page=Pages.FirstOrDefault(p=>p.Category==value.Category); if(page!=null) SelectedPage=page; ExperienceStatus="已定位分类 · "+value.Title; }
    }
    public IReadOnlyList<FeatureItemViewModel> FeelFeatures => SelectedPage.Category is "玩家" or "钓鱼"
        ? Features.Where(f => f.Descriptor.Control == FeatureControl.Number).ToArray() : Array.Empty<FeatureItemViewModel>();
    public bool HasFeelFeatures => FeelFeatures.Count > 0;
    public IReadOnlyList<FeatureItemViewModel> DisplayFeatures => Features.Except(FeelFeatures).ToArray();
    public string ActiveFeaturesText
    {
        get {
            var titles = _toggleStates.Where(e=>e.Value.Enabled).Select(e=>FeatureRegistry.All.FirstOrDefault(f=>f.Id==e.Key)?.Title ?? e.Key).ToList();
            if(AimAssist.Enabled) titles.Add("辅助瞄准"); if(Crosshair.Enabled) titles.Add("外置准星"); if(AutoRefill) titles.Add("海鸥自动补充");
            return titles.Count==0 ? "持续功能均已关闭" : string.Join(" · ",titles);
        }
    }
    public string CombatDiagnostics { get => _combatDiagnostics; private set => Set(ref _combatDiagnostics,value); }
    public bool DiagnosticOnly
    {
        get => _diagnosticOnly;
        set { if(!Set(ref _diagnosticOnly,value))return; if(value) { AimAssist.Enabled=false; var silent=Features.FirstOrDefault(f=>f.Descriptor.Id=="combat.silent_aim"); if(silent?.Enabled==true)silent.Enabled=false; } foreach(var f in Features)f.RefreshExperienceAvailability(); }
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
    public bool AutoRefill
    {
        get => _autoRefill;
        set {
            if(value && (_runtime==null || !_capabilities.CanExecute(Capability.HostWorld) || RefillLimitInput.HasError)) { AutoRefillStatus="进入世界并填写 1–20 的整数上限后再开启"; OnPropertyChanged(); return; }
            if(!Set(ref _autoRefill,value))return;
            _autoGeneration++; if(value)AutoRefillCount=0; _lastRefill=DateTimeOffset.MinValue;
            AutoRefillStatus=value?"等待海鸥存活数量；耗尽后补充一波 8 只":"自动补充已停止";
            OnPropertyChanged(nameof(ActiveFeaturesText));
        }
    }

    public ICommand SavePresetCommand => new RelayCommand(SavePreset);
    public ICommand ApplyPresetCommand => new RelayCommand(()=>_ = ApplySelectedPresetAsync());
    public ICommand DeletePresetCommand => new RelayCommand(()=> { if(SelectedPreset==null||PresetBusy)return; _experience.Presets.Remove(SelectedPreset); Presets.Remove(SelectedPreset); SelectedPreset=Presets.FirstOrDefault(); ScheduleExperienceSave(); });
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
        foreach(var preset in _experience.Presets)Presets.Add(preset); SelectedPreset=Presets.FirstOrDefault();
        RefillLimitInput.Value=_experience.RefillLimit;
        RefillLimitInput.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NumericInput.Text)){_experience.RefillLimit=(int)RefillLimitInput.LastValidValue; ScheduleExperienceSave();}};
        _experienceSaveTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(500)};
        _experienceSaveTimer.Tick+=SaveExperienceTick;
        FunEventsPage.CleanupRequested+=()=>AutoRefill=false;
        AimAssist.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(AimAssist.Enabled)){if(DiagnosticOnly&&AimAssist.Enabled)AimAssist.Enabled=false; OnPropertyChanged(nameof(ActiveFeaturesText));}};
        Crosshair.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(Crosshair.Enabled))OnPropertyChanged(nameof(ActiveFeaturesText));};
        if(_experienceStore.Warning!=null)ExperienceStatus=_experienceStore.Warning;
        foreach(var feature in Features)AttachExperience(feature);
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
        catch(Exception e){ExperienceStatus="体验设置保存失败 · "+e.Message;return false;}
    }
    public void CloseExperience(){if(_closedExperience)return;StopExperienceAutomation();FlushExperience();_closedExperience=true; if(_experienceSaveTimer!=null){_experienceSaveTimer.Stop();_experienceSaveTimer.Tick-=SaveExperienceTick;}}
    private void SavePreset()
    {
        string name=PresetName.Trim(); if(name.Length==0||name.Length>40){ExperienceStatus="预设名称需要 1–40 个字符";return;}
        if(PresetBusy)return;
        var old=_experience.Presets.FirstOrDefault(p=>string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase));
        if(old==null&&Presets.Count>=50){ExperienceStatus="最多保存 50 套预设";return;}
        var preset=new ParameterPreset(name,CapturePreferences().ToJson());
        if(old!=null){_experience.Presets.Remove(old);Presets.Remove(old);}
        _experience.Presets.Add(preset);Presets.Add(preset);SelectedPreset=preset;ActivePresetName=name;ScheduleExperienceSave();ExperienceStatus="预设已保存 · "+name;
    }
    public async Task ApplySelectedPresetAsync()
    {
        var preset=SelectedPreset;if(preset==null||PresetBusy)return;
        PresetBusy=true;
        try{
            var parameters=TrainerUserPreferences.Parse(preset.Parameters);
            _featureActionEpoch++;
            Interlocked.Increment(ref _connectionEpoch);
            // Stop accepting aim requests before clearing all locks; no old intent may replay after clear.
            StopExperienceAutomation(); AimAssist.Disconnect();
            var runtime=_runtime;int epoch=_connectionEpoch;
            if(runtime!=null){var result=await runtime.ExecuteAsync(new FeatureCommand("locks.disable_all",JsonSerializer.SerializeToElement(new{}),Capability.OwnedPlayer),CancellationToken.None);if(runtime!=_runtime||epoch!=_connectionEpoch)return;if(!result.Succeeded){AimAssist.Connect(runtime,_capabilities);ExperienceStatus="未切换预设 · 停用失败，请检查游戏中的持续效果："+result.Message;return;}}
            MarkAllLocksDisabled("预设切换，已停用"); AimAssist.Disconnect(); RoomMembersPage.Disconnect();
            if(runtime!=null)RoomMembersPage.Connect(runtime,_capabilities);
            _restoringPreferences=true;
            try{_parameterDrafts.Clear();_preferences=parameters;ApplyPreferences(parameters);}
            finally{_restoringPreferences=false;}
            if(runtime!=null)AimAssist.Connect(runtime,_capabilities);
            _preferredIslandKey=parameters.Teleport.Text("islandKey");
            SelectedIsland=IslandOptions.FirstOrDefault(i=>i.Key==_preferredIslandKey);
            SchedulePreferencesSave();ActivePresetName=preset.Name;ExperienceStatus="已恢复「"+preset.Name+"」参数 · 持续功能请手动开启";
        }catch(Exception e){ExperienceStatus="预设恢复失败 · "+e.Message;}
        finally{PresetBusy=false;RefreshFeatures();OnPropertyChanged(nameof(ActiveFeaturesText));if(_runtime!=null&&IslandOptions.Count==0)_=LoadIslandCatalogAsync(_runtime,_connectionEpoch);}
    }
    private void LoadPointDetails()
    {
        var point=SelectedTeleportPoint;var meta=_experience.Points.FirstOrDefault(p=>p.Group==point?.Group&&p.Name==point?.Name);
        PointNote=meta?.Note??"";PointColor=meta!=null&&PointColors.Contains(meta.Color)?meta.Color:"青色";
        PointIcon=meta!=null&&PointIcons.Contains(meta.Icon)?meta.Icon:"⌖";PointFavorite=meta?.Favorite??false;
    }
    private IEnumerable<TrainerTeleportPoint> SortPoints(IEnumerable<TrainerTeleportPoint> points) => PointSort switch{
        "名称"=>points.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase),
        "分组"=>points.OrderBy(p=>p.Group,StringComparer.CurrentCultureIgnoreCase).ThenBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase),
        "最近添加"=>points.Reverse(),
        _=>points.OrderByDescending(p=>_experience.Points.Any(m=>m.Group==p.Group&&m.Name==p.Name&&m.Favorite)).ThenBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase)
    };
    private bool PointMatches(TrainerTeleportPoint point,string query) => query.Length==0 || (point.Name+" "+point.Group+" "+_experience.Points.FirstOrDefault(m=>m.Group==point.Group&&m.Name==point.Name)?.Note).Contains(query,StringComparison.CurrentCultureIgnoreCase);
    public string ExportTeleportPoints() => JsonSerializer.Serialize(new {version=1,points=TeleportPoints.Select(p=>new{group=p.Group,name=p.Name,x=p.X,y=p.Y,z=p.Z,decoration=_experience.Points.FirstOrDefault(m=>m.Group==p.Group&&m.Name==p.Name)})},new JsonSerializerOptions{WriteIndented=true});
    public int ImportTeleportPoints(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(!root.TryGetProperty("version",out var v)||!v.TryGetInt32(out int version)||version!=1||!root.TryGetProperty("points",out var entries)||entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>2000)throw new JsonException("传送点格式或版本无效（最多 2000 个）");
        var staged=TeleportPoints.ToList();var decorations=_experience.Points.ToList();int imported=0;
        foreach(var e in entries.EnumerateArray()){
            string? group=e.TryGetProperty("group",out var g)&&g.ValueKind==JsonValueKind.String?g.GetString():null;
            string? name=e.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString():null;
            double Coordinate(string key){if(!e.TryGetProperty(key,out var c)||c.ValueKind!=JsonValueKind.Number||!c.TryGetDouble(out double d)||!double.IsFinite(d)||Math.Abs(d)>100000)throw new JsonException("坐标超出有效范围");return d;}
            if(string.IsNullOrWhiteSpace(group)||string.IsNullOrWhiteSpace(name)||group.Length>80||name.Length>80)throw new JsonException("传送点名称或分组无效");
            double x=Coordinate("x"),y=Coordinate("y"),z=Coordinate("z");
            // Merge only; a same-name point is never silently overwritten.
            if(staged.Any(p=>p.Group.Equals(group,StringComparison.CurrentCultureIgnoreCase)&&p.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)))continue;
            if(staged.Count>=2000)throw new JsonException("合并后的传送点超过 2000 个");
            staged.Add(new(group,name,x,y,z));imported++;
            if(e.TryGetProperty("decoration",out var meta)&&meta.ValueKind==JsonValueKind.Object){var d=JsonSerializer.Deserialize<PointDecoration>(meta.GetRawText());if(d!=null&&d.Note?.Length<=500&&PointColors.Contains(d.Color)&&PointIcons.Contains(d.Icon))decorations.Add(d with{Group=group,Name=name});}
        }
        _settings.Save(staged);TeleportPoints.Clear();foreach(var p in staged)TeleportPoints.Add(p);_experience.Points=decorations;ScheduleExperienceSave();PointMetadataChanged();RefreshTeleportGroups();RefreshTeleportFilter();PositionStatus=$"已导入 {imported} 个新坐标，同名坐标保留原值";return imported;
    }
    private async Task RememberSafePositionAsync()
    {
        var runtime=_runtime;int epoch=_connectionEpoch;if(runtime==null){PositionStatus="请先连接游戏";return;}
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("world.position.read",JsonSerializer.SerializeToElement(new{}),Capability.ReadOnly),CancellationToken.None);if(runtime!=_runtime||epoch!=_connectionEpoch)return;
            if(result.Succeeded&&result.Applied is JsonElement p&&p.ValueKind==JsonValueKind.Object){double x=p.GetProperty("x").GetDouble(),y=p.GetProperty("y").GetDouble(),z=p.GetProperty("z").GetDouble();if(!new[]{x,y,z}.All(d=>double.IsFinite(d)&&Math.Abs(d)<=100000))throw new InvalidDataException("位置无效");_safePosition=new("临时","本次安全位置",x,y,z);PositionStatus="本机安全位置已记录，仅在当前世界有效";}else PositionStatus=result.Message;
        }catch(Exception e){PositionStatus="记录安全位置失败 · "+e.Message;}
    }
    internal void StopExperienceAutomation(){AutoRefill=false;_autoGeneration++;_spinWasEnabled=false;_curveAppliedSpeed=null;}
    internal void ResetExperienceWorld(){StopExperienceAutomation();_safePosition=null;_lastLivingPosition=_deathPosition=null;_lastSampleAlive=false;TrackDeathPosition=false;DeathPositionStatus="世界已切换，临时位置已清空";CombatDiagnostics="世界已切换，等待读取新会话";}
    public void UpdateExperienceSession(JsonElement? raw)
    {
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
            if(valid){AutoRefillStatus=$"存活 {alive} 只 · 已补充 {AutoRefillCount}/{RefillLimitInput.LastValidValue:0} 波";if(alive==0)_=RefillSeagullsAsync();}
            else{AutoRefill=false;AutoRefillStatus="海鸥数量数据无效，自动补充已停止";}
        }}
        OnPropertyChanged(nameof(ActiveFeaturesText));
    }
    private async Task RefillSeagullsAsync()
    {
        if(!AutoRefill||_closedExperience||_experienceWorkBusy||DateTimeOffset.UtcNow-_lastRefill<TimeSpan.FromSeconds(3))return;
        if(RefillLimitInput.HasError||AutoRefillCount>=RefillLimitInput.LastValidValue){AutoRefill=false;AutoRefillStatus="已达到补充上限，请手动重新开启";return;}
        var runtime=_runtime;int epoch=_connectionEpoch,generation=_autoGeneration;
        var card=FunEventsPage.Events.FirstOrDefault(e=>e.Id=="seagull-migration");if(runtime==null||card?.CanExecute!=true||!FunEventsPage.CanExecuteWave)return;
        _experienceWorkBusy=true;_lastRefill=DateTimeOffset.UtcNow;
        try{var result=await FunEventsPage.ExecuteWaveAsync("seagull-migration");if(runtime!=_runtime||epoch!=_connectionEpoch||generation!=_autoGeneration)return;
            if(result){AutoRefillCount++;AutoRefillStatus=$"已补充第 {AutoRefillCount} 波（每波 8 只）";}
            else{AutoRefill=false;AutoRefillStatus="补充未成功，已停止；请检查趣味事件结果";}
        }catch(Exception e){if(runtime==_runtime&&epoch==_connectionEpoch){AutoRefill=false;AutoRefillStatus="自动补充失败 · "+e.Message;}}
        finally{_experienceWorkBusy=false;}
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
        var live=Features.FirstOrDefault(f=>f.IsSpin);if(live?.IsBusy==true)return;
        double speed=live?.LastValidSpinSpeed??(_preferences.Features.TryGetValue("player.spin.lock",out var p)?p.Number("spinSpeed")??4:4);
        double desired=SpinCurveValue(SpinCurve,speed,(now-_spinStarted).TotalSeconds);
        if(SpinCurve=="固定"&&_curveAppliedSpeed==null||_curveAppliedSpeed is double prior&&Math.Abs(prior-desired)<0.03)return;
        var runtime=_runtime;int epoch=_connectionEpoch;_experienceWorkBusy=true;
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("player.spin.lock",FeatureItemViewModel.SpinArguments(true,desired),Capability.OwnedPlayer),CancellationToken.None);
            if(runtime!=_runtime||epoch!=_connectionEpoch)return;
            if(result.Succeeded)_curveAppliedSpeed=desired;else{SpinCurve="固定";ExperienceStatus="旋转曲线暂停 · "+result.Message;}
        }catch(Exception e){if(runtime==_runtime&&epoch==_connectionEpoch)ExperienceStatus="旋转曲线失败 · "+e.Message;}
        finally{_experienceWorkBusy=false;}
    }
    private async Task SampleDeathPositionAsync(DateTimeOffset now)
    {
        var runtime=_runtime;int epoch=_connectionEpoch;if(runtime==null||!_capabilities.CanExecute(Capability.HostWorld))return;
        _deathSampleBusy=true;_lastDeathSample=now;
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("players.catalog",JsonSerializer.SerializeToElement(new{}),Capability.HostWorld),CancellationToken.None);
            if(runtime!=_runtime||epoch!=_connectionEpoch||!TrackDeathPosition)return;
            if(!result.Succeeded||result.Applied is not JsonElement p){DeathPositionStatus="本机状态读取失败";return;}
            ApplyDeathPositionSample(p);
        }catch(Exception e){if(runtime==_runtime&&epoch==_connectionEpoch)DeathPositionStatus="本机状态读取失败 · "+e.Message;}
        finally{_deathSampleBusy=false;}
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
        _spinStopPending=true;
        try{var result=await runtime.ExecuteAsync(new FeatureCommand("player.spin.lock",FeatureItemViewModel.SpinArguments(false,4),Capability.OwnedPlayer),CancellationToken.None);
            if(runtime==_runtime&&epoch==_connectionEpoch&&result.Succeeded)ApplyRememberedToggleState("player.spin.lock",new(false,result.Message,"已关闭",true));
        }catch(Exception e){ExperienceStatus="停止旋转失败 · "+e.Message;}
        finally{_spinStopPending=false;}
    }
}
