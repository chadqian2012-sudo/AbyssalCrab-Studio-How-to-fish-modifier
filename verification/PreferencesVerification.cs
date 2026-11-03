using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HowToFishTrainer.App;
using HowToFishTrainer.Core;

sealed class PreferencesRuntime : IGameRuntimeClient
{
    public readonly ConcurrentQueue<FeatureCommand> Commands = new();
    public bool HasCatalog = true, MissingItem, MissingSight;
    public string? HoldFeature, FailFeature, ThrowFeature;
    public readonly Queue<(TaskCompletionSource<RuntimeCommandResult> Completion, RuntimeCommandResult Result)> Pending = new();
    public void Complete(bool succeeded = true)
    {
        var pending = Pending.Dequeue();
        pending.Completion.SetResult(succeeded ? pending.Result : new RuntimeCommandResult(false,"fixture failure",null,null,false));
    }
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken token)
    {
        Commands.Enqueue(command);
        if(command.FeatureId==ThrowFeature)throw new IOException("fixture execution error");
        object data = new { };
        if (command.FeatureId == "catalog.items") data = new { items = HasCatalog ? new object[] {
            new {id=5,name="Apple",fish=false,kind="食品"},
            new {id=MissingItem?12:11,name="Test Rifle",fish=false,kind="枪械",weapon=new{damage=9},attachments=new{
                sights=new[]{new{id=MissingSight?"other":"scope",name="Scope"}},barrels=new[]{new{id="barrel",name="Barrel"}},ammo=new[]{new{id="ammo",name="Ammo"}},extendedMag=true,laser=true},skins=new[]{new{id="skin",name="Blue",rarity="rare"}}},
            new {id=21,name="Test Fish",fish=true,kind="鱼类"}
        } : Array.Empty<object>() };
        if (command.FeatureId == "catalog.islands") data = new {islands=new[]{
            new {index=1,key="island:a",name="A",rawName="A",available=true,runtimeIndex=1,sceneBuildIndex=2},
            new {index=2,key="island:b",name="B",rawName="B",available=true,runtimeIndex=2,sceneBuildIndex=3}}};
        if (command.FeatureId is "players.list" or "players.catalog") data = new {players=Array.Empty<object>()};
        if (command.FeatureId == "features.proofs") data = new {features=Array.Empty<object>()};
        if (command.FeatureId == "casino.status") data = new {outcomeMode="black",multiplier=4,remainingRounds=0,available=true};
        if (command.FeatureId == "casino.configure") data = new {
            outcomeMode=command.Arguments.GetProperty("outcomeMode").GetString(),
            multiplier=command.Arguments.GetProperty("multiplier"),
            remainingRounds=command.Arguments.GetProperty("rounds").GetInt32(),available=true};
        var json = JsonSerializer.SerializeToElement(data);
        var result = new RuntimeCommandResult(command.FeatureId!=FailFeature,command.FeatureId==FailFeature?"fixture failure":"fixture",json,json,false);
        if(command.FeatureId==HoldFeature)
        {
            var completion=new TaskCompletionSource<RuntimeCommandResult>();Pending.Enqueue((completion,result));
            return new ValueTask<RuntimeCommandResult>(completion.Task);
        }
        return ValueTask.FromResult(result);
    }
}
sealed class CountingPreferencesStore : IUserPreferencesStore
{
    public string? Warning => null;
    public int Saves; public string Json = "{}"; public bool Fail;
    public TrainerUserPreferences Load() => TrainerUserPreferences.Parse(Json);
    public void Save(TrainerUserPreferences value) { if(Fail) throw new IOException("fixture write denied"); Saves++; Json=value.ToJson(); }
}
sealed class DelayedAimRuntime : IGameRuntimeClient
{
    public readonly List<FeatureCommand> Commands=new();
    public readonly Queue<TaskCompletionSource<RuntimeCommandResult>> Pending=new();
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command,CancellationToken token)
    {
        Commands.Add(command);var completion=new TaskCompletionSource<RuntimeCommandResult>();Pending.Enqueue(completion);
        return new ValueTask<RuntimeCommandResult>(completion.Task);
    }
    public void Complete(bool success=true) {var json=JsonSerializer.SerializeToElement(new{});Pending.Dequeue().SetResult(new RuntimeCommandResult(success,"fixture",json,json,false));}
}

static class PreferencesVerification
{
    static readonly List<string> Checks = new();
    static void Check(bool ok, string name) { if(!ok) throw new Exception("FAILED: "+name); Checks.Add(name); }
    static void Pump(int milliseconds = 0)
    {
        var watch = Stopwatch.StartNew();
        do { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); if(milliseconds>0) Thread.Sleep(5); }
        while(watch.ElapsedMilliseconds < milliseconds);
    }
    static FeatureItemViewModel Feature(MainViewModel vm, string id)
    {
        string category = MainViewModel.DisplayCategory(id, FeatureRegistry.Find(id)!.Category);
        vm.SelectedPage = vm.Pages.Single(p=>p.Category==category);
        return vm.Features.SingleOrDefault(f=>f.Descriptor.Id==id) ?? throw new Exception("No generic feature: "+id+" in "+category);
    }
    static string[] Writable(PreferencesRuntime runtime) => runtime.Commands.Where(c=>
        !new[]{"catalog.items","catalog.islands","players.list","players.catalog","players.inspect","features.proofs","casino.status","fun.event.availability","fun.event.catalog"}.Contains(c.FeatureId)).Select(c=>c.FeatureId).ToArray();
    static object Invoke(object obj,string name,params object[] args) => obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(obj,args)!;
    static IEnumerable<T> Visuals<T>(DependencyObject parent) where T:DependencyObject
    {
        if(parent is T value) yield return value;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            foreach(var child in Visuals<T>(VisualTreeHelper.GetChild(parent,i))) yield return child;
    }

    static void AwaitSession(Task task)
    {
        var watch=Stopwatch.StartNew();
        while(!task.IsCompleted){if(watch.ElapsedMilliseconds>10000)throw new Exception("Last-session fixture timeout");Pump(1);}
        task.GetAwaiter().GetResult();
    }
    static void CloseSession(MainViewModel vm){vm.CloseExperience();vm.ClosePreferences();}
    static void VerifyLastSession()
    {
        var caps=CapabilityMatrix.For(SessionMode.Host,false);
        string speedId=FeatureRegistry.All.First(f=>f.Control==FeatureControl.Number&&f.Id.EndsWith(".multiplier",StringComparison.Ordinal)&&f.Title.Contains("移动速度")).Id;
        TrainerUserPreferences Saved(bool enabled=true)
        {
            var p=new TrainerUserPreferences();
            p.General.Put("soundEnabled",false);p.General.Put("page","玩家");p.General.Put("refillLimit",4);
            p.AimAssist.Put("strength",77);p.AimAssist.Put("angle",4.25);p.AimAssist.Put("distance",68.5);
            p.AimAssist.Put("trigger","ads");p.AimAssist.Put("leadEnabled",false);p.AimAssist.Put("headLockEnabled",true);p.AimAssist.Put("targetTeammates",false);
            p.Crosshair.Put("shape","圆环");p.Crosshair.Put("color","红色");p.Crosshair.Put("size",24);
            p.SlotMachine.Put("rarity",2);p.Roulette.Put("outcome","green");p.Roulette.Put("rounds",10);p.Roulette.Put("useOriginalMultiplier",false);p.Roulette.Put("multiplier",17);
            ParameterValues Values(string id){var values=new ParameterValues();p.Features[id]=values;return values;}
            Values(speedId).Put("value",2.75);
            var spin=Values("player.spin.lock");spin.Put("spinSpeed",7.25);spin.Put("spinAutoJump",true);
            var magic=Values("combat.silent_aim");magic.Put("angle",12.5);magic.Put("distance",222.75);magic.Put("strength",83.25);magic.Put("magicTrigger","ads");magic.Put("targetTeammates",true);
            p.Session.Put("snapshot",true);
            foreach(string id in new[]{"player.health.lock","player.fullness.lock","player.spin.lock","combat.silent_aim"})p.Session.Put("toggle:"+id,enabled);
            foreach(string key in new[]{"aimAssist","crosshair","combatFeedback","slotMachine","roulette","autoRefill","trackDeathPosition"})p.Session.Put(key,enabled);
            p.Session.Put("diagnosticOnly",false);
            p.Session.Put("targetRoomMember",true);p.Session.Put("playerKey","expired-fixture-player");
            // Persisted unknown/one-shot keys must never broaden the replay allowlist.
            foreach(var f in FeatureRegistry.All.Where(f=>f.Control is FeatureControl.Generator or FeatureControl.Coordinates||f.Id.StartsWith("money.",StringComparison.Ordinal)&&f.Control!=FeatureControl.Toggle))p.Session.Put("toggle:"+f.Id,true);
            return p;
        }
        MainViewModel Connected(TrainerUserPreferences p,out PreferencesRuntime runtime,out CountingPreferencesStore store)
        {
            store=new CountingPreferencesStore{Json=p.ToJson()};var vm=new MainViewModel(preferencesStore:store);
            runtime=new PreferencesRuntime();vm.Connect(runtime,caps);Pump(10);return vm;
        }
        bool Successful(MainViewModel vm)=>vm.ExperienceStatus.Contains("已恢复")&&!vm.ExperienceStatus.Contains("失败");
        bool Failed(MainViewModel vm)=>new[]{"失败","未完成","未全部","异常","未切换"}.Any(vm.ExperienceStatus.Contains);
        void NoOneShot(PreferencesRuntime runtime)
        {
            var reads=new[]{"world.position.read","players.position.read","players.inspect","players.list","players.catalog","fun.event.availability","fun.event.catalog"};
            Check(runtime.Commands.Where(c=>!reads.Contains(c.FeatureId)).All(c=>!c.FeatureId.StartsWith("generation.",StringComparison.Ordinal)&&(!c.FeatureId.StartsWith("money.",StringComparison.Ordinal)||FeatureRegistry.Find(c.FeatureId)?.Control==FeatureControl.Toggle)
                &&!c.FeatureId.StartsWith("world.",StringComparison.Ordinal)&&!c.FeatureId.StartsWith("fun.event.",StringComparison.Ordinal)
                &&!c.FeatureId.StartsWith("players.",StringComparison.Ordinal)),
                "Last-session replay sends no generation, teleport, currency, animal-event or member mutation commands: "+string.Join(",",runtime.Commands.Select(c=>c.FeatureId)));
        }
        var legacy=new MainViewModel(preferencesStore:new CountingPreferencesStore());
        var legacyRuntime=new PreferencesRuntime();legacy.Connect(legacyRuntime,caps);Pump(10);
        Check(!legacy.CanRestoreLastSession,"Legacy preferences have no session snapshot to restore");
        AwaitSession(legacy.RestoreLastSessionAsync());Check(Writable(legacyRuntime).Length==0,"Restoring a missing snapshot sends no mutation");CloseSession(legacy);

        var seed=Saved();string original=seed.ToJson();var store=new CountingPreferencesStore{Json=original};
        var restored=new MainViewModel(preferencesStore:store);
        Check(!restored.CanRestoreLastSession&&!restored.AimAssist.Enabled&&!restored.Crosshair.Enabled,"A stored session starts offline with continuous features disabled");
        var runtime=new PreferencesRuntime();restored.Connect(runtime,caps);Pump(10);
        Check(restored.CanRestoreLastSession&&Writable(runtime).Length==0,"Connecting only enables the restore button, without replaying saved commands");
        Check(!Feature(restored,"player.health.lock").Enabled&&!Feature(restored,"player.fullness.lock").Enabled,"Disk toggle intentions are not activated during startup or category rebuilding");
        Feature(restored,speedId).ValueInput.Value=1.5;restored.SoundEnabled=true;restored.AimAssist.StrengthInput.Value=23;restored.FlushPreferences();
        Check(seed.ToJson()==original,"Editing and autosaving the current session do not alias the supplied snapshot");
        AwaitSession(restored.RestoreLastSessionAsync());Pump(10);
        Check(Writable(runtime).FirstOrDefault()=="locks.disable_all","Explicit restore first waits for all locks to stop");
        Check(!restored.SoundEnabled&&Feature(restored,speedId).ValueInput.Value==2.75,"Explicit restore uses the independent last-session parameters rather than newer editor autosaves");
        Check(Feature(restored,"player.health.lock").Enabled&&Feature(restored,"player.fullness.lock").Enabled,"Independent toggles survive cross-category restore");
        var spinRestored=Feature(restored,"player.spin.lock");var magicRestored=Feature(restored,"combat.silent_aim");
        Check(spinRestored.Enabled&&spinRestored.SpinSpeedText=="7.25"&&spinRestored.SpinAutoJump,"Spin restore retains decimal speed and model jump option");
        var spinCommand=runtime.Commands.Last(c=>c.FeatureId=="player.spin.lock"&&c.Arguments.GetProperty("enabled").GetBoolean());
        Check(spinCommand.Arguments.GetProperty("rotationsPerSecond").GetDouble()==7.25,"Spin enable transmits the restored speed");
        var magicCommand=runtime.Commands.Last(c=>c.FeatureId=="combat.silent_aim"&&c.Arguments.GetProperty("enabled").GetBoolean());
        Check(magicRestored.Enabled&&magicCommand.Arguments.GetProperty("angle").GetDouble()==12.5&&magicCommand.Arguments.GetProperty("distance").GetDouble()==222.75
            &&magicCommand.Arguments.GetProperty("strength").GetDouble()==83.25&&magicCommand.Arguments.GetProperty("trigger").GetString()=="ads"&&magicCommand.Arguments.GetProperty("targetTeammates").GetBoolean(),
            "Magic-bullet replay uses restored angle, distance, strength, trigger and teammate selection");
        Check(runtime.Commands.Any(c=>c.FeatureId==speedId&&c.Arguments.GetProperty("value").GetDouble()==2.75),"Saved multiplier parameters are applied only by the explicit restore");
        Check(restored.AimAssist.Enabled&&restored.AimAssist.Strength==77&&restored.AimAssist.MaximumDistance==68.5&&!restored.AimAssist.LeadEnabled&&restored.AimAssist.HeadLockEnabled,
            "Aim restore enables only after loading the saved targeting parameters");
        Check(restored.Crosshair.Enabled&&restored.Crosshair.Color=="红色"&&restored.Crosshair.Size==24,"Crosshair restores its saved state and style");
        Check(restored.CombatFeedback.Active&&restored.FunEventsPage.SlotMachine.Enabled&&restored.FunEventsPage.SlotMachine.TargetRarity==2,"Combat feedback and slot-machine states restore through their owners");
        Check(restored.FunEventsPage.Roulette.Active&&restored.FunEventsPage.Roulette.CustomMultiplier==17,"Roulette restore applies the saved result, rounds and multiplier");
        Check(restored.AutoRefill&&restored.RefillLimitInput.Value==4&&restored.TrackDeathPosition,"Explicit restore may re-enable saved local automation switches without generating a wave immediately");
        NoOneShot(runtime);Check(Successful(restored),"Successful replay is reported after its command sequence completes");CloseSession(restored);

        foreach(bool disconnect in new[]{false,true})
        {
            var captureStore=new CountingPreferencesStore();var source=new MainViewModel(preferencesStore:captureStore);var live=new PreferencesRuntime();source.Connect(live,caps);Pump(10);
            Feature(source,"player.health.lock").Enabled=true;Feature(source,"combat.silent_aim");Feature(source,"player.fullness.lock").Enabled=true;source.Crosshair.Enabled=true;source.AutoRefill=true;source.TrackDeathPosition=true;
            if(disconnect)source.Disconnect();
            CloseSession(source);var captured=TrainerUserPreferences.Parse(captureStore.Json);
            Check(captured.Session.Boolean("snapshot")==true&&captured.Session.Boolean("toggle:player.health.lock")==true&&captured.Session.Boolean("toggle:player.fullness.lock")==true,
                "Capture before "+(disconnect?"disconnect":"close cleanup")+" preserves cross-category toggles");
            Check(captured.Session.Boolean("crosshair")==true&&captured.Session.Boolean("autoRefill")==true&&captured.Session.Boolean("trackDeathPosition")==true,
                "Cleanup does not overwrite the saved local switch intentions: "+disconnect);
            var next=Connected(captured,out var nextRuntime,out _);Check(Writable(nextRuntime).Length==0,"A captured session remains inactive after restart: "+disconnect);
            AwaitSession(next.RestoreLastSessionAsync());Check(Feature(next,"player.health.lock").Enabled&&Feature(next,"player.fullness.lock").Enabled,"Captured cross-page toggles restore after an explicit click: "+disconnect);CloseSession(next);
        }
        var off=Connected(Saved(false),out var offRuntime,out _);Feature(off,"player.health.lock").Enabled=true;off.Crosshair.Enabled=true;
        AwaitSession((Task)Invoke(off.FunEventsPage.SlotMachine,"ApplyAsync"));AwaitSession((Task)Invoke(off.FunEventsPage.Roulette,"ApplyAsync"));
        Check(off.FunEventsPage.SlotMachine.Enabled&&off.FunEventsPage.Roulette.Active,"The off-state fixture actually starts with active slot-machine and roulette owners");
        AwaitSession(off.RestoreLastSessionAsync());Check(!Feature(off,"player.health.lock").Enabled&&!Feature(off,"player.fullness.lock").Enabled&&!off.AimAssist.Enabled&&!off.Crosshair.Enabled&&!off.AutoRefill&&!off.TrackDeathPosition
            &&!off.FunEventsPage.SlotMachine.Enabled&&!off.FunEventsPage.Roulette.Active,
            "Saved off states remain off and clear newer current-session switches");CloseSession(off);

        var rouletteOnly=Saved(false);rouletteOnly.Features.Clear();rouletteOnly.Session.Put("roulette",true);
        var casino=Connected(rouletteOnly,out var casinoRuntime,out _);
        int priorCasinoReads=casinoRuntime.Commands.Count(c=>c.FeatureId=="casino.status");casinoRuntime.HoldFeature="casino.status";
        AwaitSession(casino.RestoreLastSessionAsync());
        Check(casinoRuntime.Pending.Count==0&&casinoRuntime.Commands.Count(c=>c.FeatureId=="casino.status")==priorCasinoReads,
            "A roulette-only restore does not launch a competing held status refresh during lock cleanup");
        Check(casino.FunEventsPage.Roulette.Active&&casinoRuntime.Commands.Any(c=>c.FeatureId=="casino.configure"&&c.Arguments.GetProperty("multiplier").GetInt32()==17),
            "A roulette-only snapshot applies its configuration even when a new casino-status read would stay pending");
        Check(Successful(casino),"A roulette-only restore reports success after its actual configure result");NoOneShot(casinoRuntime);CloseSession(casino);
        foreach(string failed in new[]{"locks.disable_all","player.health.lock","combat.aim_assist"})
        {
            var vm=Connected(Saved(),out var fake,out _);fake.FailFeature=failed;AwaitSession(vm.RestoreLastSessionAsync());
            Check(Failed(vm),"A failed restore step is reported explicitly rather than as complete success: "+failed);
            if(failed=="locks.disable_all")Check(Writable(fake).SequenceEqual(new[]{"locks.disable_all"}),"Failed lock shutdown prevents all subsequent restore writes");
            if(failed=="player.health.lock")Check(!Feature(vm,"player.health.lock").Enabled,"Rejected feature enable is not published as on");
            if(failed=="combat.aim_assist")Check(!vm.AimAssist.Enabled,"Rejected aim enable stays off");CloseSession(vm);
        }
        var throwing=Connected(Saved(),out var throwingRuntime,out _);throwingRuntime.ThrowFeature="locks.disable_all";AwaitSession(throwing.RestoreLastSessionAsync());
        Check(Failed(throwing)&&Writable(throwingRuntime).SequenceEqual(new[]{"locks.disable_all"}),"A thrown shutdown error aborts restore without claiming success");
        Check(throwing.AimAssist.CanConfigure,"A thrown shutdown error leaves the connected aim controls available");
        throwingRuntime.ThrowFeature=null;throwing.AimAssist.Enabled=true;Pump(10);
        Check(throwingRuntime.Commands.Any(c=>c.FeatureId=="combat.aim_assist"&&c.Arguments.GetProperty("enabled").GetBoolean()),"Manual aim enable still reaches the runtime after restore shutdown throws");CloseSession(throwing);

        var waitingAim=Connected(Saved(),out var aimingRuntime,out _);aimingRuntime.HoldFeature="combat.aim_assist";
        Task aiming=waitingAim.RestoreLastSessionAsync();Pump(10);
        Check(!aiming.IsCompleted&&aimingRuntime.Pending.Count==1,"The restore task awaits native aim confirmation");
        aimingRuntime.Complete(false);AwaitSession(aiming);Check(!waitingAim.AimAssist.Enabled&&Failed(waitingAim),"Late native aim failure is reflected in final restore status");CloseSession(waitingAim);
        foreach(string held in new[]{"locks.disable_all","player.health.lock"})
        foreach(string transition in new[]{"world","reconnect","close"})
        {
            var vm=Connected(Saved(),out var oldRuntime,out _);oldRuntime.HoldFeature=held;Task pending=vm.RestoreLastSessionAsync();Pump(10);
            Check(!pending.IsCompleted&&oldRuntime.Pending.Count==1,"Restore is awaiting the held step: "+held+"/"+transition);
            Check(!Feature(vm,"player.health.lock").Enabled&&!Feature(vm,"player.fullness.lock").Enabled&&!vm.AimAssist.Enabled,
                "Unconfirmed and later restore intentions are not published as enabled while awaiting "+held+"/"+transition);
            PreferencesRuntime? newer=null;
            if(transition=="world")vm.ResetWorldContext();else if(transition=="reconnect"){newer=new PreferencesRuntime();vm.Connect(newer,caps);}else CloseSession(vm);
            int mutations=Writable(oldRuntime).Length;oldRuntime.Complete();AwaitSession(pending);Pump(10);
            Check(Writable(oldRuntime).Length==mutations&&(newer==null||Writable(newer).Length==0),"A stale restore sends no remaining commands after "+transition+" at "+held);
            Check(!vm.PresetBusy,"Canceled restore releases its local busy state: "+held+"/"+transition);
            CloseSession(vm);
        }
        var diagnostic=Saved();diagnostic.Session.Put("diagnosticOnly",true);var observe=Connected(diagnostic,out var observingRuntime,out _);
        AwaitSession(observe.RestoreLastSessionAsync());Check(observe.DiagnosticOnly&&!observe.AimAssist.Enabled&&!Feature(observe,"combat.silent_aim").Enabled,"Restored observation mode preserves its aim/magic mutual exclusion");
        Check(!observingRuntime.Commands.Any(c=>(c.FeatureId is "combat.aim_assist" or "combat.silent_aim")&&c.Arguments.TryGetProperty("enabled",out var on)&&on.ValueKind==JsonValueKind.True),
            "Observation-mode restore never briefly enables aim or magic before turning them off");CloseSession(observe);

        var invalidSnapshot=Saved();invalidSnapshot.Session.Put("snapshot","true");var invalidSession=Connected(invalidSnapshot,out var invalidRuntime,out _);
        Check(!invalidSession.CanRestoreLastSession,"A malformed snapshot marker does not enable restore");AwaitSession(invalidSession.RestoreLastSessionAsync());
        Check(Writable(invalidRuntime).Length==0,"A malformed snapshot marker cannot replay commands");CloseSession(invalidSession);
        var invalidDistance=Saved(false);invalidDistance.AimAssist.Put("distance",88.5);var bounded=Connected(invalidDistance,out _,out _);
        AwaitSession(bounded.RestoreLastSessionAsync());
        Check(bounded.AimAssist.MaximumDistance==35&&bounded.AimAssist.Strength==77&&bounded.AimAssist.AcquireAngle==4.25,
            "An out-of-range saved aim distance falls back independently without widening the 10-80 range");CloseSession(bounded);
        var invalidFlag=Saved(false);invalidFlag.Session.Put("toggle:player.health.lock","yes");invalidFlag.Session.Put("toggle:player.fullness.lock",true);
        var partialSession=Connected(invalidFlag,out _,out _);AwaitSession(partialSession.RestoreLastSessionAsync());
        Check(!Feature(partialSession,"player.health.lock").Enabled&&Feature(partialSession,"player.fullness.lock").Enabled,"One malformed session flag does not discard a valid neighboring toggle");CloseSession(partialSession);

        var preset=Connected(Saved(),out var presetRuntime,out _);Feature(preset,"player.health.lock").Enabled=true;
        preset.PresetName="session fixture";preset.SavePresetCommand.Execute(null);int presetStart=Writable(presetRuntime).Length;
        AwaitSession(preset.ApplySelectedPresetAsync());
        Check(Writable(presetRuntime).Skip(presetStart).All(id=>id=="locks.disable_all")&&!Feature(preset,"player.health.lock").Enabled&&!preset.AimAssist.Enabled,
            "Applying a normal parameter preset does not replay its captured continuous switches");CloseSession(preset);
        var copy=seed.Copy();copy.Session.Put("toggle:player.health.lock",false);
        Check(seed.Session.Boolean("toggle:player.health.lock")==true&&TrainerUserPreferences.Parse(seed.ToJson()).Session.Boolean("snapshot")==true,"Session dictionaries are copied deeply and round-trip under schema version one");
    }

    [STAThread] public static int Main(string[] args)
    {
        string root=args[0],recovery=args[1];
        AssemblyLoadContext.Default.Resolving+=(ctx,name)=> {string path=Path.Combine(recovery,"extracted",name.Name+".dll");return File.Exists(path)?ctx.LoadFromAssemblyPath(path):null;};
        if(Environment.GetEnvironmentVariable("ABYSS_SESSION_ONLY")=="1")
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            string isolated=Path.Combine(root,"verification","last-session-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(isolated);
            Exception? sessionFailure=null;try{VerifyLastSession();}catch(Exception error){sessionFailure=error;}
            File.WriteAllText(Path.Combine(isolated,"results.json"),JsonSerializer.Serialize(new{passed=sessionFailure==null,checks=Checks.Count,tests=Checks,failure=sessionFailure?.ToString(),
                isolated_storage=true,fixture_only=true,game_writes_performed=false,ui_created=false,assembly_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"Last-session verification: {Checks.Count} checks; passed={sessionFailure==null}; {isolated}");if(sessionFailure!=null)Console.WriteLine(sessionFailure);return sessionFailure==null?0:1;
        }
        bool codeOnly=Environment.GetEnvironmentVariable("ABYSS_CODE_ONLY")=="1";
        Application? app=codeOnly?null:new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string temporary=Path.Combine(root,"verification","preferences-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string path=Path.Combine(temporary,"user-preferences.json");
        var store=new FileUserPreferencesStore(path);
        var vm=new MainViewModel(preferencesStore:store);
        Check(!File.Exists(path),"first launch is read only");
        var numericIds=FeatureRegistry.All.Where(f=>f.Control==FeatureControl.Number && f.Id!="world.island.load" && f.Id!="fun.slot_machine.rarity").Select(f=>f.Id).ToArray();
        var expected=new Dictionary<string,double>();
        foreach(string id in numericIds)
        {
            var f=Feature(vm,id);var input=f.ValueInput;
            double value=input.IntegerOnly?input.Value+1:input.Value+0.25;
            if(!input.IsValidValue(value)) value=input.Minimum??input.Value;
            input.Value=value; f.SaveAfter=true; expected[id]=value;
            input.Text="";
            vm.SelectedPage=vm.Pages.First(p=>p!=vm.SelectedPage);
            f=Feature(vm,id);
            Check(f.ValueInput.Text==""&&f.Value==value,id+" invalid draft and last valid value survive page rebuild");
        }
        var spin=Feature(vm,"player.spin.lock");spin.SpinSpeedText="7.25";spin.SpinSpeedText="bad";spin.SpinAutoJump=true;
        vm.SelectedPage=vm.Pages.Last();spin=Feature(vm,"player.spin.lock");
        Check(spin.SpinSpeedText=="bad","spin draft survives page change");
        Check(spin.SpinAutoJump,"model auto-jump choice survives page change");
        vm.SoundEnabled=false; vm.SafeLanding=false;
        vm.PointGroup="custom group";vm.PointName="unfinished point";vm.TeleportPointQuery="search point";
        vm.CurrentXInput.Text="-10.25";vm.CurrentYInput.Text="3.125";vm.CurrentZInput.Text="44.5";vm.CurrentYInput.Text="-";
        vm.IslandQuery="B";
        vm.AimAssist.StrengthInput.Text="81";vm.AimAssist.AngleInput.Text="9.75";vm.AimAssist.DistanceInput.Text="44.5";
        vm.AimAssist.SelectedTrigger=vm.AimAssist.TriggerModes.Last();vm.AimAssist.Enabled=true;
        vm.AimAssist.LeadEnabled=false;vm.AimAssist.HeadLockEnabled=true;
        vm.Crosshair.Shape="圆环";vm.Crosshair.Color="红色";vm.Crosshair.Size=27;vm.Crosshair.Enabled=true;
        var roulette=vm.FunEventsPage.Roulette;
        roulette.SelectedOutcomeMode=roulette.OutcomeModes.Single(o=>o.Value=="green");
        roulette.SelectedRoundMode=roulette.RoundModes.Single(o=>o.Rounds==10);roulette.UseOriginalMultiplier=false;
        roulette.MultiplierInput.Text="17";roulette.MultiplierInput.Text="17.5";
        vm.FunEventsPage.SlotMachine.TargetRarity=2;
        vm.RoomMembersPage.HealthScope="all";vm.RoomMembersPage.FullnessScope="local";vm.RoomMembersPage.PoisonScope="selected";
        vm.RoomMembersPage.FireScope="all";vm.RoomMembersPage.DropScope="selected";vm.RoomMembersPage.RareScope="local";vm.RoomMembersPage.SearchText="room filter";
        var runtime=new PreferencesRuntime();var caps=CapabilityMatrix.For(SessionMode.Host,false);
        vm.Connect(runtime,caps);Pump(30);
        var item=Feature(vm,"generation.item");
        Check(item.Options.Any(o=>o.Id==11),"fixture catalog loaded");
        item.SelectedOption=item.Options.Single(o=>o.Id==11);
        item.SelectedSight=item.SelectedOption.Sights.Single();item.SelectedBarrel=item.SelectedOption.Barrels.Single();
        item.SelectedAmmo=item.SelectedOption.Ammo.Single();
        if(item.SelectedOption.Skins.Any()) item.SelectedSkin=item.SelectedOption.Skins.Single();
        item.ExtendedMagazine=true;item.LaserSight=true;item.SaveAfter=true;
        item.QuantityInput.Text="7";item.DistanceInput.Text="4.25";item.SearchText="Test";item.SelectedKind="枪械";
        var fish=Feature(vm,"generation.fish");fish.SelectedOption=fish.Options.Single(o=>o.Id==21);fish.QuantityInput.Text="3";
        fish.DistanceInput.Text="5.5";fish.WeightInput.Text="2.75";fish.DeadFish=true;fish.Rare=true;fish.SaveAfter=true;
        vm.SelectedIsland=vm.IslandOptions.Single(o=>o.Key=="island:b");
        vm.Disconnect();Pump();
        Check(Feature(vm,"generation.item").Quantity==7,"disconnect preserves generator parameters");
        vm.SelectedPage=vm.Pages.Single(p=>p.Category=="钓鱼");
        vm.ClosePreferences();
        Check(File.Exists(path),"close flushes preferences");
        string saved=File.ReadAllText(path);
        Check(!saved.Contains("\"enabled\"",StringComparison.OrdinalIgnoreCase)&&!saved.Contains("playerKey"),"snapshot excludes activation/session state");
        var restored=new MainViewModel(preferencesStore:new FileUserPreferencesStore(path));
        Check(restored.SelectedPage.Category=="钓鱼"&&!restored.SoundEnabled,"page and sound restored");
        foreach(var pair in expected)
        {
            var f=Feature(restored,pair.Key);
            Check(f.Value==pair.Value&&!f.ValueInput.HasError&&f.SaveAfter,pair.Key+" valid value and save option restored");
        }
        spin=Feature(restored,"player.spin.lock");
        Check(spin.SpinSpeedText=="7.25"&&!spin.Enabled,"spin restores last valid speed while off");
        Check(spin.SpinAutoJump&&!spin.Enabled,"model auto-jump preference restores without starting spin");
        Check(restored.CurrentX==-10.25&&restored.CurrentY==3.125&&restored.CurrentZ==44.5,"coordinates restored from last valid numbers");
        Check(restored.PointGroup=="custom group"&&restored.PointName=="unfinished point"&&restored.TeleportPointQuery=="search point"&&!restored.SafeLanding,"teleport text and configuration restored");
        Check(restored.AimAssist.Strength==81&&restored.AimAssist.AcquireAngle==9.75&&restored.AimAssist.MaximumDistance==44.5&&restored.AimAssist.SelectedTrigger.Value=="ads"&&!restored.AimAssist.Enabled,"aim parameters restore while off with ADS-only triggering");
        Check(!restored.AimAssist.LeadEnabled&&restored.AimAssist.HeadLockEnabled&&!restored.AimAssist.Enabled,"lead and head preferences restore without activating camera tracking");
        Check(restored.Crosshair.Shape=="圆环"&&restored.Crosshair.Color=="红色"&&restored.Crosshair.Size==27&&!restored.Crosshair.Enabled,"crosshair parameters restored while off");
        roulette=restored.FunEventsPage.Roulette;
        Check(roulette.SelectedOutcomeMode.Value=="green"&&roulette.SelectedRoundMode.Rounds==10&&!roulette.UseOriginalMultiplier&&roulette.CustomMultiplier==17,"roulette restores editable configuration");
        Check(restored.FunEventsPage.SlotMachine.TargetRarity==2&&!restored.FunEventsPage.SlotMachine.Enabled,"slot rarity restored while off");
        var m=restored.RoomMembersPage;
        Check(m.HealthScope=="all"&&m.FullnessScope=="local"&&m.PoisonScope=="selected"&&m.FireScope=="all"&&m.DropScope=="selected"&&m.RareScope=="local"&&m.SearchText=="room filter","member policy scopes and search restored");
        Check(!restored.TargetRoomMember&&m.SelectedMember==null,"session targets not restored");
        // Capture offline a second time; a temporarily empty catalog must not erase IDs/configuration.
        Feature(restored,"generation.item");restored.SoundEnabled=true;restored.FlushPreferences();
        var reconnectRuntime=new PreferencesRuntime();restored.Connect(reconnectRuntime,caps);Pump(30);
        item=Feature(restored,"generation.item");
        Check(item.SelectedOption?.Id==11&&item.Quantity==7&&item.Distance==4.25&&item.SelectedKind=="枪械"&&item.SearchText=="Test","item ID and numeric/filter state deferred until catalog ready");
        Check(item.SelectedSight?.Id=="scope"&&item.SelectedBarrel?.Id=="barrel"&&item.SelectedAmmo?.Id=="ammo"&&item.ExtendedMagazine&&item.LaserSight&&item.SaveAfter,"weapon configuration preserved across offline rebuilds");
        Check(item.SelectedSkin?.Id=="skin","skin selection restored by ID");
        fish=Feature(restored,"generation.fish");
        Check(fish.SelectedOption?.Id==21&&fish.Quantity==3&&fish.Distance==5.5&&fish.WeightMultiplier==2.75&&fish.DeadFish&&fish.Rare&&fish.SaveAfter,"fish configuration restored");
        Check(restored.SelectedIsland?.Key=="island:b"&&restored.IslandQuery=="B","island key restored after async catalog");
        roulette.RefreshCommand.Execute(null);Pump();
        Check(roulette.SelectedOutcomeMode.Value=="green"&&!roulette.UseOriginalMultiplier&&roulette.CustomMultiplier==17,"runtime readback does not overwrite pending roulette editors");
        string[] writes=Writable(reconnectRuntime);
        Check(writes.Length==0,"restoration and connection send no mutation commands: "+string.Join(",",writes));
        item.ExecuteCommand.Execute(null);Pump();
        var generated=reconnectRuntime.Commands.Last(c=>c.FeatureId=="generation.item");
        Check(generated.Arguments.GetProperty("value").GetInt32()==11&&generated.Arguments.GetProperty("quantity").GetInt32()==7&&generated.Arguments.GetProperty("distance").GetDouble()==4.25,"manual generation uses restored parameters");
        spin=Feature(restored,"player.spin.lock");spin.Enabled=true;Pump();
        Check(reconnectRuntime.Commands.Any(c=>c.FeatureId=="player.spin.lock"&&c.Arguments.GetProperty("enabled").GetBoolean()&&c.Arguments.GetProperty("rotationsPerSecond").GetDouble()==7.25),"manual spin activation uses restored decimal speed");
        spin.Enabled=false;Pump();restored.ClosePreferences();

        var missing=new MainViewModel(preferencesStore:new FileUserPreferencesStore(path));
        var missingRuntime=new PreferencesRuntime {MissingItem=true};missing.Connect(missingRuntime,caps);Pump();
        var unavailable=Feature(missing,"generation.item");int before=missingRuntime.Commands.Count;
        unavailable.ExecuteCommand.Execute(null);Pump();
        Check(unavailable.SelectedOption==null&&missingRuntime.Commands.Count==before&&unavailable.Status.Contains("重新选择"),"missing saved catalog ID blocks unintended substitute generation");
        missing.SoundEnabled=false;missing.ClosePreferences();
        var recoveredCatalog=new MainViewModel(preferencesStore:new FileUserPreferencesStore(path));
        recoveredCatalog.Connect(new PreferencesRuntime(),caps);Pump();
        Check(Feature(recoveredCatalog,"generation.item").SelectedOption?.Id==11,"unavailable selection remains remembered for later catalog recovery");
        recoveredCatalog.ClosePreferences();

        var missingAttachment=new MainViewModel(preferencesStore:new FileUserPreferencesStore(path));
        var missingAttachmentRuntime=new PreferencesRuntime {MissingSight=true};missingAttachment.Connect(missingAttachmentRuntime,caps);Pump();
        var incomplete=Feature(missingAttachment,"generation.item");before=missingAttachmentRuntime.Commands.Count;
        incomplete.ExecuteCommand.Execute(null);Pump();
        Check(incomplete.SelectedOption?.Id==11&&incomplete.SelectedSight==null&&missingAttachmentRuntime.Commands.Count==before,"missing attachment blocks silent fallback to default");
        missingAttachment.SoundEnabled=true;missingAttachment.ClosePreferences();
        var attachmentRecovery=new MainViewModel(preferencesStore:new FileUserPreferencesStore(path));attachmentRecovery.Connect(new PreferencesRuntime(),caps);Pump();
        Check(Feature(attachmentRecovery,"generation.item").SelectedSight?.Id=="scope","missing attachment ID survives save for future recovery");attachmentRecovery.ClosePreferences();

        // Real bindings can send transient nulls while ItemsSource changes. Preserve pending IDs offline.
        if(!codeOnly){
        app!.Resources=(ResourceDictionary)Application.LoadComponent(new Uri("/HowToFishTrainer;component/themes/theme.xaml",UriKind.Relative));
        var boundVm=new MainViewModel(preferencesStore:new FileUserPreferencesStore(path));Feature(boundVm,"generation.item");
        using var license=new HowToFishTrainer.App.Licensing.LicenseService(temporary,"1.1.37");
        var window=new MainWindow(license) {DataContext=boundVm,ShowActivated=false,ShowInTaskbar=false,IsHitTestVisible=false,Left=-20000,Top=-20000};
        ((DispatcherTimer)typeof(MainWindow).GetField("_timer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Stop();
        window.Show();window.UpdateLayout();Pump(40);
        boundVm.SoundEnabled=false;boundVm.FlushPreferences();boundVm.Connect(new PreferencesRuntime(),caps);Pump(40);window.UpdateLayout();
        item=Feature(boundVm,"generation.item");Pump();
        Check(item.SelectedOption?.Id==11&&item.SelectedKind=="枪械"&&item.SelectedSight?.Id=="scope"&&item.SelectedSkin?.Id=="skin","real WPF offline-to-online binding preserves option/kind/attachment/skin");
        boundVm.SelectedPage=boundVm.Pages.Single(p=>p.Category=="世界传送");Pump();window.UpdateLayout();
        var pointNameEditor=Visuals<TextBox>(window).Single(box=>(box.ToolTip as string)=="坐标名称");
        pointNameEditor.Text="last keystroke without blur";
        Check(boundVm.PointName=="last keystroke without blur","point name commits each keystroke without losing focus");
        boundVm.ClosePreferences();window.Close();
        var nameRestored=new MainViewModel(preferencesStore:new FileUserPreferencesStore(path));
        Check(nameRestored.PointName=="last keystroke without blur","last focused text edit survives close");nameRestored.ClosePreferences();
        var productionStore=new CountingPreferencesStore();
        var productionWindow=new MainWindow(license,productionStore) {ShowActivated=false,ShowInTaskbar=false,IsHitTestVisible=false,Left=-20000,Top=-20000};
        var productionVm=(MainViewModel)productionWindow.DataContext;
        Check(!((DispatcherTimer)typeof(MainWindow).GetField("_timer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(productionWindow)!).IsEnabled,"preview constructor leaves polling timer stopped without a manual Stop");
        Pump(1150);
        Check(typeof(MainWindow).GetField("_bridge",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(productionWindow)==null,"preview does not attach to a running game after one poll interval");
        productionWindow.Show();productionVm.PointName="real window close flush";productionWindow.Close();
        Check(productionStore.Saves==1&&TrainerUserPreferences.Parse(productionStore.Json).Teleport.Text("pointName")=="real window close flush","production constructor and OnClosed use injected persistence store");
        }

        var malformed=TrainerUserPreferences.Parse("{\"schemaVersion\":1,\"general\":{\"soundEnabled\":false},\"aimAssist\":{\"strength\":\"bad\",\"angle\":99,\"distance\":45.25},\"features\":{\"player.spin.lock\":{\"spinSpeed\":99},\"invalid\":5},\"crosshair\":{\"size\":-5,\"color\":\"unknown\"}}");
        var partial=new MainViewModel(preferencesStore:new CountingPreferencesStore {Json=malformed.ToJson()});
        Check(!partial.SoundEnabled&&partial.AimAssist.Strength==60&&partial.AimAssist.AcquireAngle==8&&partial.AimAssist.MaximumDistance==45.25,"bad individual fields do not discard valid neighbors");
        Check(Feature(partial,"player.spin.lock").SpinSpeedText=="4"&&partial.Crosshair.Size==16&&partial.Crosshair.Color=="白色","corrupt range/choice falls back without clamping");partial.ClosePreferences();
        var counter=new CountingPreferencesStore();var delayed=new MainViewModel(preferencesStore:counter);
        delayed.PointName="a";Pump(80);delayed.PointName="ab";Pump(80);delayed.PointName="abc";
        Check(counter.Saves==0,"autosave debounces quick edits");Pump(570);
        Check(counter.Saves==1&&TrainerUserPreferences.Parse(counter.Json).Teleport.Text("pointName")=="abc","autosave fires after 500 ms idle");
        delayed.PointName="close immediately";delayed.ClosePreferences();
        Check(counter.Saves==2&&TrainerUserPreferences.Parse(counter.Json).Teleport.Text("pointName")=="close immediately","close flushes before debounce deadline");
        delayed.PointName="after close";Pump(550);Check(counter.Saves==2,"close detaches observers and stops timer");
        var failure=new CountingPreferencesStore {Fail=true};var failing=new MainViewModel(preferencesStore:failure);failing.SoundEnabled=false;
        Check(!failing.FlushPreferences()&&failing.ModeStatus.Contains("参数保存失败"),"write failure is visible and nonfatal");failure.Fail=false;
        Check(failing.FlushPreferences()&&failure.Saves==1&&!failing.ModeStatus.Contains("参数保存失败"),"dirty state can retry after write failure");failing.ClosePreferences();

        var first=new TrainerUserPreferences();first.General.Put("soundEnabled",false);
        string diskPath=Path.Combine(temporary,"atomic.json");var disk=new FileUserPreferencesStore(diskPath);disk.Load();disk.Save(first);
        var second=TrainerUserPreferences.Parse(first.ToJson());second.General.Put("soundEnabled",true);disk.Save(second);
        Check(File.Exists(diskPath+".bak")&&TrainerUserPreferences.Parse(File.ReadAllText(diskPath+".bak")).General.Boolean("soundEnabled")==false,"atomic replacement keeps preceding valid backup");
        File.WriteAllText(diskPath,"{broken");var backup=new FileUserPreferencesStore(diskPath);
        Check(backup.Load().General.Boolean("soundEnabled")==false&&backup.Warning!.Contains("备份"),"broken primary falls back to backup");backup.Save(second);
        Check(TrainerUserPreferences.Parse(File.ReadAllText(diskPath+".bak")).General.Boolean("soundEnabled")==false,"broken primary does not overwrite good backup");
        Check(Directory.GetFiles(temporary,"*.tmp").Length==0,"atomic writes leave no temporary files");
        string future=Path.Combine(temporary,"future.json");File.WriteAllText(future,"{\"schemaVersion\":99}");var futureStore=new FileUserPreferencesStore(future);futureStore.Load();
        bool refused=false;try{futureStore.Save(first);}catch(IOException){refused=true;}
        Check(refused&&File.ReadAllText(future).Contains("99"),"newer schema is preserved");
        var invalidSetter=new NumericInput("test",3,1,5);invalidSetter.Value=99;
        Check(invalidSetter.Value==99&&invalidSetter.Text=="99"&&invalidSetter.HasError&&invalidSetter.LastValidValue==3,"programmatic invalid value retains existing draft semantics and separate valid fallback");
        var aimRuntime=new DelayedAimRuntime();var aim=new AimAssistViewModel();aim.Connect(aimRuntime,caps);
        aim.AngleInput.Text="bad";aim.Enabled=true;
        Check(!aim.Enabled&&aimRuntime.Commands.Count==0,"invalid checkbox activation rejected and rolled back");
        aim.AngleInput.Text="8.25";aim.Enabled=true;
        Check(aimRuntime.Commands.Count==1&&aimRuntime.Commands[0].Arguments.GetProperty("enabled").GetBoolean(),"aim checkbox immediately submits native enable");
        aim.Enabled=false;aimRuntime.Complete();Pump();
        Check(aimRuntime.Commands.Count==2&&!aimRuntime.Commands[1].Arguments.GetProperty("enabled").GetBoolean(),"quick on-off queues latest disable after in-flight enable");
        aimRuntime.Complete();Pump();Check(!aim.Enabled&&!aim.Busy,"rapid toggle settles off");
        aim.Enabled=true;aimRuntime.Complete(false);Pump();Check(!aim.Enabled&&aim.Status.Contains("失败"),"native enable failure rolls checkbox back");
        aim.Enabled=true;aimRuntime.Complete();Pump();aim.AngleInput.Text="bad";aim.Enabled=false;
        Check(!aimRuntime.Commands.Last().Arguments.GetProperty("enabled").GetBoolean()&&aimRuntime.Commands.Last().Arguments.GetProperty("angle").GetDouble()==8.25,"disable remains available with invalid editor draft");
        aimRuntime.Complete();Pump();aim.Disconnect();
        var headRuntime=new DelayedAimRuntime();var head=new AimAssistViewModel();
        head.HeadLockEnabled=true;head.Connect(headRuntime,caps);
        Check(!head.Enabled&&headRuntime.Commands.Count==0,"connection with restored head preference sends no writes");
        head.HeadLockEnabled=false;headRuntime.Complete();Pump();head.HeadLockEnabled=true;
        Check(head.Enabled&&headRuntime.Commands.Last().Arguments.GetProperty("headLockEnabled").GetBoolean()&&headRuntime.Commands.Last().Arguments.GetProperty("leadEnabled").GetBoolean(),"manual head lock toggle enables aim and transmits default lead");
        head.LeadEnabled=false;headRuntime.Complete();Pump();
        Check(!headRuntime.Commands.Last().Arguments.GetProperty("leadEnabled").GetBoolean(),"lead changes during pending apply queue latest configuration");
        headRuntime.Complete();Pump();head.Disconnect();
        var legacyAimStore=new CountingPreferencesStore{Json="{\"schemaVersion\":1,\"aimAssist\":{\"trigger\":\"always\"}}"};
        var migratedAim=new MainViewModel(preferencesStore:legacyAimStore);
        Check(migratedAim.AimAssist.SelectedTrigger.Value=="ads"&&migratedAim.AimAssist.TriggerModes.Count==1&&!migratedAim.AimAssist.Enabled,"legacy always preference migrates to ADS-only without activation");migratedAim.ClosePreferences();
        string json=JsonSerializer.Serialize(new{passed=true,checks=Checks.Count,failures=0,tests=Checks,test_directory=temporary,game_writes_performed=false,fixture_only=true,code_only=codeOnly,ui_bindings_tested=!codeOnly,assembly_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(root,"verification",codeOnly?"function-audit/preferences-results.json":"preferences-results.json"),json);
        Console.WriteLine($"Preferences verification: {Checks.Count} checks passed; code-only={codeOnly}");app?.Shutdown();return 0;
    }
}
