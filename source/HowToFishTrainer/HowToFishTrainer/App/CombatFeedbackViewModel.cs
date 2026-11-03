using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

[JsonConverter(typeof(CombatFeedbackOptionsConverter))]
public sealed class CombatFeedbackOptions
{
    public bool Streaks { get; set; } = true;
    public bool Markers { get; set; } = true;
    public bool HeadSound { get; set; } = true;
    public bool KillSound { get; set; } = true;
    public bool Numbers { get; set; } = true;
    public bool DynamicCrosshair { get; set; }
    public double ComboWindow { get; set; } = 8;
    public double Volume { get; set; } = .35;
    public double Opacity { get; set; } = .9;
    public double Scale { get; set; } = 1;
}

public sealed class CombatFeedbackViewModel : INotifyPropertyChanged
{
    private IGameRuntimeClient? _runtime;
    private bool _host, _active, _busy, _loading;
    private int _epoch;
    private CombatFeedbackOptions _options = new();
    private string _status = "进入房主/单人世界后，点击开启；重启后保持关闭";
    private string _summary = "尚无本次战斗记录";
    private (long Hits,long Heads,long Kills,long Damage,long Combo,long Best)? _lastSummary;
    public NumericInput ComboWindowInput { get; } = new("连杀间隔（秒）",8,1,30);
    public NumericInput VolumeInput { get; } = new("提示音音量",.35,0,1);
    public NumericInput OpacityInput { get; } = new("反馈透明度",.9,.1,1);
    public NumericInput ScaleInput { get; } = new("反馈大小倍率",1,.5,2);
    public bool Streaks { get=>_options.Streaks;set{if(_options.Streaks==value)return;_options.Streaks=value;Changed();} }
    public bool Markers { get=>_options.Markers;set{if(_options.Markers==value)return;_options.Markers=value;Changed();} }
    public bool HeadSound { get=>_options.HeadSound;set{if(_options.HeadSound==value)return;_options.HeadSound=value;Changed();} }
    public bool KillSound { get=>_options.KillSound;set{if(_options.KillSound==value)return;_options.KillSound=value;Changed();} }
    public bool Numbers { get=>_options.Numbers;set{if(_options.Numbers==value)return;_options.Numbers=value;Changed();} }
    public bool DynamicCrosshair { get=>_options.DynamicCrosshair;set{if(_options.DynamicCrosshair==value)return;_options.DynamicCrosshair=value;Changed();} }
    public bool Active { get=>_active;private set {if(_active==value)return;_active=value;Notify();Notify(nameof(CanStop));} }
    public bool CanApply=>_runtime!=null&&_host&&!_busy&&!HasError;
    public bool CanStop=>_runtime!=null&&!_busy&&Active;
    public bool HasError=>NumericInput.FirstError(ComboWindowInput,VolumeInput,OpacityInput,ScaleInput).Length>0;
    public string Status { get=>_status;private set{if(_status==value)return;_status=value;Notify();} }
    public string Summary { get=>_summary;private set{if(_summary==value)return;_summary=value;Notify();} }
    public ICommand ApplyCommand { get; }
    public ICommand StopCommand { get; }
    public event Action? SettingsChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public CombatFeedbackViewModel()
    {
        ApplyCommand=new RelayCommand(()=>_ = ApplyAsync());
        StopCommand=new RelayCommand(()=>_ = StopAsync());
        foreach(var input in new[]{ComboWindowInput,VolumeInput,OpacityInput,ScaleInput})
            input.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NumericInput.Text)){Notify(nameof(CanApply));Notify(nameof(HasError));Changed("Parameters");}};
    }
    private void Changed([CallerMemberName]string? name=null)
    {Notify(name);if(!_loading)SettingsChanged?.Invoke();}
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    public void Load(CombatFeedbackOptions? options)
    {
        if(options==null)return;
        _loading=true;
        try
        {
            _options=options;
            foreach(var name in new[]{nameof(Streaks),nameof(Markers),nameof(HeadSound),nameof(KillSound),nameof(Numbers),nameof(DynamicCrosshair)})Notify(name);
            if(ComboWindowInput.IsValidValue(options.ComboWindow))ComboWindowInput.Value=options.ComboWindow;
            if(VolumeInput.IsValidValue(options.Volume))VolumeInput.Value=options.Volume;
            if(OpacityInput.IsValidValue(options.Opacity))OpacityInput.Value=options.Opacity;
            if(ScaleInput.IsValidValue(options.Scale))ScaleInput.Value=options.Scale;
        }
        finally{_loading=false;}
    }
    public CombatFeedbackOptions Capture()=>new(){Streaks=Streaks,Markers=Markers,HeadSound=HeadSound,KillSound=KillSound,Numbers=Numbers,DynamicCrosshair=DynamicCrosshair,
        ComboWindow=ComboWindowInput.LastValidValue,Volume=VolumeInput.LastValidValue,Opacity=OpacityInput.LastValidValue,Scale=ScaleInput.LastValidValue};
    public void Connect(IGameRuntimeClient runtime,bool host)
    {_epoch++;_runtime=runtime;_host=host;_busy=false;Active=false;_lastSummary=null;Summary="尚无本次战斗记录";Status=host?"已连接 · 点击开启/应用战斗反馈":"当前会话缺少本机房主伤害结算";Notify(nameof(CanApply));}
    public void Disconnect()
    {_epoch++;_runtime=null;_host=false;_busy=false;Active=false;Status="战斗反馈已停止 · 等待连接";Notify(nameof(CanApply));}
    public void ResetSession(){_epoch++;_busy=false;Active=false;_lastSummary=null;Summary="当前会话战斗统计已清空";Notify(nameof(CanApply));Notify(nameof(CanStop));}
    public async Task ApplyAsync()
    {
        if(!CanApply){if(HasError)Status=NumericInput.FirstError(ComboWindowInput,VolumeInput,OpacityInput,ScaleInput);return;}
        var p=Capture();
        await SendAsync(new{enabled=true,streaks=p.Streaks,markers=p.Markers,headSound=p.HeadSound,killSound=p.KillSound,numbers=p.Numbers,
            dynamicCrosshair=p.DynamicCrosshair,comboWindow=p.ComboWindow,volume=p.Volume,opacity=p.Opacity,scale=p.Scale},true);
    }
    public async Task StopAsync()
    {if(_runtime==null||_busy)return;await SendAsync(new{enabled=false},false);}
    private async Task SendAsync(object args,bool enable)
    {
        var runtime=_runtime;int epoch=_epoch;if(runtime==null)return;
        _busy=true;Notify(nameof(CanApply));Notify(nameof(CanStop));
        try
        {
            var result=await runtime.ExecuteAsync(new FeatureCommand("combat.feedback.configure",JsonSerializer.SerializeToElement(args),Capability.ReadOnly),CancellationToken.None);
            if(runtime!=_runtime||epoch!=_epoch)return;
            if(result.Succeeded)Active=enable;
            Status=result.Succeeded?(enable?"已开启 · 仅统计本机对生物的实际伤害，参数修改后点击应用":"战斗反馈已关闭"):"应用失败 · "+result.Message;
        }
        catch(Exception error){if(runtime==_runtime&&epoch==_epoch)Status="战斗反馈设置失败 · "+error.Message;}
        finally{if(runtime==_runtime&&epoch==_epoch){_busy=false;Notify(nameof(CanApply));Notify(nameof(CanStop));}}
    }
    public void UpdateSession(JsonElement session)
    {
        if(session.ValueKind!=JsonValueKind.Object||!session.TryGetProperty("combatFeedback",out var value)||value.ValueKind!=JsonValueKind.Object)
        {if(Active){Active=false;Status="当前运行时没有提供战斗反馈数据，请使用配套运行时";}return;}
        static long Number(JsonElement p,string key)=>p.TryGetProperty(key,out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetInt64(out var count)&&count>=0?count:0;
        if(value.TryGetProperty("enabled",out var on)&&on.ValueKind==JsonValueKind.False&&Active){Active=false;Status="运行时已停止战斗反馈，可手动重新开启";}
        var counters=(Hits:Number(value,"hits"),Heads:Number(value,"headshots"),Kills:Number(value,"kills"),Damage:Number(value,"damage"),Combo:Number(value,"combo"),Best:Number(value,"bestCombo"));
        if(_lastSummary==counters)return;
        _lastSummary=counters;
        Summary=$"命中 {counters.Hits} · 爆头 {counters.Heads} · 击败 {counters.Kills} · 实际伤害 {counters.Damage}\n当前连杀 {counters.Combo} · 本次最高 {counters.Best}";
    }
}
