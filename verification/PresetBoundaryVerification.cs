using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HowToFishTrainer.App;
using HowToFishTrainer.Core;

sealed class PresetBoundaryRuntime : IGameRuntimeClient
{
    public readonly Queue<TaskCompletionSource<RuntimeCommandResult>> Clears = new();
    public readonly List<FeatureCommand> Commands = new();
    public string? HoldFeature;
    public readonly Queue<TaskCompletionSource<RuntimeCommandResult>> Pending = new();
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand c, CancellationToken token)
    {
        Commands.Add(c);
        if(c.FeatureId=="locks.disable_all" && Clears.Count!=0)return new(Clears.Dequeue().Task);
        if(c.FeatureId==HoldFeature){var pending=new TaskCompletionSource<RuntimeCommandResult>();Pending.Enqueue(pending);return new(pending.Task);}
        object data=c.FeatureId switch {
            "catalog.items"=>new{items=Array.Empty<object>()},
            "catalog.islands"=>new{islands=Array.Empty<object>()},
            "features.proofs"=>new{features=Array.Empty<object>()},
            _=>new{applied=true}
        };
        var json=JsonSerializer.SerializeToElement(data);return new(new RuntimeCommandResult(true,"fixture",json,json,false));
    }
    public static RuntimeCommandResult Result(bool success=true)
    {var json=JsonSerializer.SerializeToElement(new{applied=true});return new(success,"fixture clear",json,json,false);}
}
sealed class PresetBoundaryStore : IUserPreferencesStore
{
    public string Json="{}";
    public string? Warning=>null;
    public TrainerUserPreferences Load()=>TrainerUserPreferences.Parse(Json);
    public void Save(TrainerUserPreferences p){}
}
static class PresetBoundaryVerification
{
    static readonly List<string> Passed=new(),Failed=new();
    static string Root="";
    static void Check(bool condition,string name)=>(condition?Passed:Failed).Add(name);
    static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    static MainViewModel Create(PresetBoundaryRuntime r,string json="{}")
    {
        var vm=new MainViewModel(preferencesStore:new PresetBoundaryStore{Json=json},experiencePath:Path.Combine(Root,Guid.NewGuid().ToString("N"),"experience.json"));
        vm.Connect(r,CapabilityMatrix.For(SessionMode.Host,false));Pump();return vm;
    }
    static void Select(MainViewModel vm,string name,double distance)
    {var p=new ParameterPreset(name,JsonSerializer.Serialize(new{aimAssist=new{distance}}));vm.Presets.Add(p);vm.SelectedPreset=p;}
    static void Close(MainViewModel vm){vm.Disconnect();vm.ClosePreferences();vm.CloseExperience();Pump();}
    static async Task Emergency(MainViewModel vm,PresetBoundaryRuntime runtime)
    {
        var method=typeof(MainViewModel).GetMethod("EmergencyDisableAsync",BindingFlags.Public|BindingFlags.Instance);
        if(method!=null){await (Task)method.Invoke(vm,new object?[]{runtime})!;return;}
        // Exercise the old window's exact command ordering without constructing a window.
        typeof(MainViewModel).GetMethod("StopExperienceAutomation",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(vm,null);
        vm.AimAssist.Disconnect();vm.Crosshair.Enabled=false;
        try
        {
            var result=await runtime.ExecuteAsync(new FeatureCommand("locks.disable_all",JsonSerializer.SerializeToElement(new{}),Capability.OwnedPlayer),CancellationToken.None);
            vm.ModeStatus=result.Succeeded?"已紧急停用全部持续修改":"紧急停用失败 · "+result.Message;
            if(result.Succeeded)vm.MarkAllLocksDisabled("已通过紧急快捷键停用");
        }
        catch(Exception e){vm.ModeStatus="紧急停用失败 · "+e.Message;}
    }
    [STAThread] static int Main(string[] a)
    {
        Root=a[0];Directory.CreateDirectory(Root);SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        {
            var r=new PresetBoundaryRuntime();var completion=new TaskCompletionSource<RuntimeCommandResult>();r.Clears.Enqueue(completion);
            var vm=Create(r);
            var descriptor=FeatureRegistry.All.First(d=>d.Control==FeatureControl.Toggle&&d.RequiredCapability==Capability.OwnedPlayer&&d.Id!="combat.aim_assist"&&d.Id!="combat.silent_aim"&&d.Id!="player.spin.lock"&&RuntimeFeatureSupport.ImplementedIds.Contains(d.Id));
            vm.SelectedPage=vm.Pages.Single(p=>p.Category==MainViewModel.DisplayCategory(descriptor.Id,descriptor.Category));
            Select(vm,"world A",66);var pending=vm.ApplySelectedPresetAsync();
            Check(vm.PresetBusy&&!pending.IsCompleted,"Apply holds busy while the clear command is pending");
            vm.ResetWorldContext();Check(!vm.PresetBusy,"World change releases the stale preset busy state");
            Check(vm.AimAssist.CanConfigure&&!vm.AimAssist.Enabled,"World change resumes the paused aim owner without enabling it");
            Check(vm.Features.Single(f=>f.Descriptor.Id==descriptor.Id).CanInteract,"World change rebuilds invalidated feature controls after cancelling a preset");
            vm.AimAssist.DistanceInput.Text="47.5";
            completion.SetResult(PresetBoundaryRuntime.Result());Pump();
            Check(pending.IsCompleted&&vm.AimAssist.MaximumDistance==47.5,"Old-world success does not overwrite new-world edited parameters");
            Check(vm.ActivePresetName!="world A","Old-world completion does not mark an unapplied preset active");Close(vm);
        }
        {
            var r=new PresetBoundaryRuntime();var completion=new TaskCompletionSource<RuntimeCommandResult>();r.Clears.Enqueue(completion);
            var vm=Create(r);Select(vm,"world A failure",66);var pending=vm.ApplySelectedPresetAsync();vm.ResetWorldContext();
            string previous=vm.ExperienceStatus;
            completion.SetException(new IOException("old-world failure"));Pump();
            Check(pending.IsCompleted&&vm.ExperienceStatus==previous,"Old-world exception does not replace current status");Close(vm);
        }
        {
            var oldRuntime=new PresetBoundaryRuntime();var oldCompletion=new TaskCompletionSource<RuntimeCommandResult>();oldRuntime.Clears.Enqueue(oldCompletion);
            var vm=Create(oldRuntime);Select(vm,"old connection",66);var oldTask=vm.ApplySelectedPresetAsync();
            var currentRuntime=new PresetBoundaryRuntime();var currentCompletion=new TaskCompletionSource<RuntimeCommandResult>();currentRuntime.Clears.Enqueue(currentCompletion);
            vm.Disconnect();vm.Connect(currentRuntime,CapabilityMatrix.For(SessionMode.Host,false));Pump();
            Check(!vm.PresetBusy,"Reconnect does not inherit old preset busy state");
            Select(vm,"current connection",51);var newTask=vm.ApplySelectedPresetAsync();
            Check(vm.PresetBusy&&!newTask.IsCompleted,"Current connection can start a preset before the old reply arrives");
            string status=vm.ExperienceStatus;
            oldCompletion.SetException(new IOException("old connection failure"));Pump();
            Check(oldTask.IsCompleted&&vm.PresetBusy&&!newTask.IsCompleted,"Old finalizer cannot clear the newer preset busy state");
            Check(vm.ExperienceStatus==status,"Old connection failure cannot overwrite current preset status");
            currentCompletion.SetResult(PresetBoundaryRuntime.Result());Pump();
            Check(newTask.IsCompleted&&!vm.PresetBusy&&vm.AimAssist.MaximumDistance==51,"Only current preset completion applies its parameters");Close(vm);
        }
        {
            var r=new PresetBoundaryRuntime();var completion=new TaskCompletionSource<RuntimeCommandResult>();r.Clears.Enqueue(completion);
            var vm=Create(r);Select(vm,"closing",66);var pending=vm.ApplySelectedPresetAsync();double distance=vm.AimAssist.MaximumDistance;
            vm.CloseExperience();completion.SetResult(PresetBoundaryRuntime.Result());Pump();
            Check(pending.IsCompleted&&vm.AimAssist.MaximumDistance==distance&&!vm.PresetBusy,"Close invalidates pending preset completion");
            int writes=r.Commands.Count(c=>c.FeatureId=="locks.disable_all");vm.ApplySelectedPresetAsync().GetAwaiter().GetResult();Pump();
            Check(r.Commands.Count(c=>c.FeatureId=="locks.disable_all")==writes,"Closed experience cannot dispatch another preset command");Close(vm);
        }
        {
            var r=new PresetBoundaryRuntime();var completion=new TaskCompletionSource<RuntimeCommandResult>();r.Clears.Enqueue(completion);
            var vm=Create(r);Select(vm,"rejected clear",66);var task=vm.ApplySelectedPresetAsync();completion.SetResult(PresetBoundaryRuntime.Result(false));Pump();
            Check(task.IsCompleted&&!vm.PresetBusy&&vm.AimAssist.CanConfigure&&vm.AimAssist.MaximumDistance==35,"Current clear rejection restores controls without applying a preset");Close(vm);
        }
        foreach(bool emergencySucceeded in new[]{true,false})
        {
            var saved=new TrainerUserPreferences();saved.Session.Put("snapshot",true);
            saved.Session.Put("toggle:player.health.lock",true);saved.Session.Put("toggle:player.fullness.lock",true);
            var r=new PresetBoundaryRuntime{HoldFeature="player.health.lock"};var vm=Create(r,saved.ToJson());
            var restore=vm.RestoreLastSessionAsync();Pump();
            Check(!restore.IsCompleted&&r.Pending.Count==1,"Restore waits on its first feature before emergency stop: "+emergencySucceeded);
            var clear=new TaskCompletionSource<RuntimeCommandResult>();r.Clears.Enqueue(clear);
            var emergency=Emergency(vm,r);Pump();
            int sent=r.Commands.Count;
            r.Pending.Dequeue().SetResult(PresetBoundaryRuntime.Result());Pump();
            Check(restore.IsCompleted&&r.Commands.Count==sent,"Emergency stop cancels remaining restore commands before its clear returns: "+emergencySucceeded);
            Check(vm.PresetBusy&&!emergency.IsCompleted,"The canceled restore cannot release the emergency stop operation: "+emergencySucceeded);
            clear.SetResult(PresetBoundaryRuntime.Result(emergencySucceeded));Pump();
            Check(emergency.IsCompleted&&!vm.PresetBusy&&vm.AimAssist.CanConfigure,"Emergency completion releases controls in the current connection: "+emergencySucceeded);
            Check(!r.Commands.Any(c=>c.FeatureId=="player.fullness.lock"),"Emergency stop never reopens a later saved feature: "+emergencySucceeded);
            Close(vm);
        }
        {
            var saved=new TrainerUserPreferences();saved.Session.Put("snapshot",true);saved.Session.Put("aimAssist",true);
            var r=new PresetBoundaryRuntime{HoldFeature="combat.aim_assist"};var vm=Create(r,saved.ToJson());
            var restore=vm.RestoreLastSessionAsync();Pump();
            Check(!restore.IsCompleted&&r.Pending.Count==1,"World-transition fixture reaches the pending aim-enable receipt");
            vm.ResetWorldContext();int sent=r.Commands.Count;
            r.Pending.Dequeue().SetResult(PresetBoundaryRuntime.Result());Pump();
            Check(restore.IsCompleted&&r.Commands.Count==sent&&!vm.AimAssist.Enabled&&vm.AimAssist.CanConfigure,
                "The previous world's pending aim reply cannot enable or disconnect the new world's aim controls");
            r.HoldFeature=null;vm.AimAssist.Enabled=true;Pump();
            Check(vm.AimAssist.Enabled&&r.Commands.Count(c=>c.FeatureId=="combat.aim_assist")==2,"Manual aim enable remains functional after world cancellation");Close(vm);
        }
        {
            var r=new PresetBoundaryRuntime();var vm=Create(r);
            var firstClear=new TaskCompletionSource<RuntimeCommandResult>();r.Clears.Enqueue(firstClear);var first=Emergency(vm,r);
            var secondClear=new TaskCompletionSource<RuntimeCommandResult>();r.Clears.Enqueue(secondClear);var second=Emergency(vm,r);
            Check(r.Commands.Count(c=>c.FeatureId=="locks.disable_all")==2,"A second emergency request is not blocked by the first busy operation");
            string status=vm.ModeStatus;firstClear.SetException(new IOException("superseded emergency"));Pump();
            Check(first.IsCompleted&&!second.IsCompleted&&vm.PresetBusy&&vm.ModeStatus==status,"An older emergency failure cannot release or overwrite a newer emergency operation");
            secondClear.SetResult(PresetBoundaryRuntime.Result());Pump();
            Check(second.IsCompleted&&!vm.PresetBusy&&vm.AimAssist.CanConfigure,"The latest emergency completes with usable controls");Close(vm);
        }
        {
            var r=new PresetBoundaryRuntime();var vm=new MainViewModel(preferencesStore:new PresetBoundaryStore());
            var stop=Emergency(vm,r);Pump();
            Check(stop.IsCompleted&&r.Commands.Count(c=>c.FeatureId=="locks.disable_all")==1&&!vm.AimAssist.CanConfigure,
                "An available bridge can still clear locks before the view model has connected");Close(vm);
        }
        {
            var oldRuntime=new PresetBoundaryRuntime();var vm=Create(oldRuntime);
            var descriptor=FeatureRegistry.Find("player.health.lock")!;
            vm.SelectedPage=vm.Pages.Single(p=>p.Category==MainViewModel.DisplayCategory(descriptor.Id,descriptor.Category));
            var oldFeature=vm.Features.Single(f=>f.Descriptor.Id==descriptor.Id);oldFeature.Enabled=true;Pump();
            Check(oldFeature.Enabled&&oldFeature.IsSuccessful,"Emergency reconnect fixture starts with a confirmed active ordinary toggle");
            var clear=new TaskCompletionSource<RuntimeCommandResult>();oldRuntime.Clears.Enqueue(clear);
            var emergency=Emergency(vm,oldRuntime);Pump();
            Check(!emergency.IsCompleted&&!oldFeature.Enabled&&!oldFeature.IsSuccessful&&oldFeature.Status=="停用请求待确认",
                "Emergency revokes reconnect intent before the runtime confirms the stop");
            vm.SuspendConnection();var currentRuntime=new PresetBoundaryRuntime();vm.Connect(currentRuntime,CapabilityMatrix.For(SessionMode.Host,false));Pump();
            var currentFeature=vm.Features.Single(f=>f.Descriptor.Id==descriptor.Id);
            Check(!currentFeature.Enabled&&!currentRuntime.Commands.Any(c=>c.FeatureId==descriptor.Id),
                "Reconnect during an emergency stop cannot replay the revoked ordinary toggle");
            currentFeature.Enabled=true;Pump();vm.ModeStatus="fixture current connection";
            int sent=currentRuntime.Commands.Count;clear.SetResult(PresetBoundaryRuntime.Result());Pump();
            Check(emergency.IsCompleted&&currentFeature.Enabled&&currentFeature.IsSuccessful&&currentFeature.CanInteract
                &&vm.ModeStatus=="fixture current connection"&&currentRuntime.Commands.Count==sent,
                "A late old emergency success cannot disable a new manual toggle or change the new connection");Close(vm);
        }
        File.WriteAllText(Path.Combine(Root,"results.json"),JsonSerializer.Serialize(new{passed=Failed.Count==0,checks=Passed.Count+Failed.Count,passedChecks=Passed,failedChecks=Failed,
            fixtureOnly=true,gameInjected=false,applicationSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Preset boundaries: {Passed.Count} passed, {Failed.Count} failed");foreach(var f in Failed)Console.WriteLine("FAILED: "+f);return Failed.Count==0?0:1;
    }
}
