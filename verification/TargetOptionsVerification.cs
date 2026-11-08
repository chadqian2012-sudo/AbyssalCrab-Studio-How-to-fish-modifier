using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HowToFishTrainer.App;
using HowToFishTrainer.Core;
using AbyssalCrab.Runtime;

sealed class OptionsRuntime : IGameRuntimeClient
{
    public readonly List<FeatureCommand> Commands = new();
    public bool Reject, Throw;
    public TaskCompletionSource<RuntimeCommandResult>? PendingMagic;
    public TaskCompletionSource<RuntimeCommandResult>? PendingProof;
    public TaskCompletionSource<RuntimeCommandResult>? PendingOverlay;
    public double? OverlayReadbackDistance;
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken token)
    {
        Commands.Add(command);
        if (command.FeatureId == "combat.silent_aim" && PendingMagic != null) return new(PendingMagic.Task);
        if (command.FeatureId == "features.proofs" && PendingProof != null) return new(PendingProof.Task);
        if (command.FeatureId == "view.entity_overlay.configure" && PendingOverlay != null) return new(PendingOverlay.Task);
        bool targeting = command.FeatureId.StartsWith("combat.") || command.FeatureId == "view.entity_overlay.configure";
        if (Throw && targeting) throw new IOException("fixture disconnected");
        object data = command.FeatureId switch {
            "catalog.items" => new { items = Array.Empty<object>() },
            "catalog.islands" => new { islands = Array.Empty<object>() },
            "features.proofs" => new { features = Array.Empty<object>() },
            "view.entity_overlay.configure" => new { distance = OverlayReadbackDistance ?? command.Arguments.GetProperty("distance").GetDouble() },
            _ => new { applied = true }
        };
        var json = JsonSerializer.SerializeToElement(data);
        return ValueTask.FromResult(new RuntimeCommandResult(!Reject || !targeting, "fixture", json, json, false));
    }
    public FeatureCommand Last(string id) => Commands.Last(c => c.FeatureId == id);
}
sealed class OptionsStore : IUserPreferencesStore
{
    public string Json = "{}";
    public string? Warning => null;
    public TrainerUserPreferences Load() => TrainerUserPreferences.Parse(Json);
    public void Save(TrainerUserPreferences p) => Json = p.ToJson();
}
static class TargetOptionsVerification
{
    static readonly List<string> Checks = new();
    static void Check(bool value, string message) { if (!value) throw new Exception("FAILED: " + message); Checks.Add(message); }
    static void Pump() { var f = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false)); Dispatcher.PushFrame(f); }
    static FeatureItemViewModel Magic(MainViewModel vm)
    {
        var descriptor = FeatureRegistry.Find("combat.silent_aim")!;
        vm.SelectedPage = vm.Pages.Single(p => p.Category == MainViewModel.DisplayCategory(descriptor.Id, descriptor.Category));
        return vm.Features.Single(f => f.IsMagicBullet);
    }
    static bool Team(FeatureCommand c) => c.Arguments.GetProperty("targetTeammates").GetBoolean();
    [STAThread] static int Main(string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Directory.CreateDirectory(args[0]);
        var caps = CapabilityMatrix.For(SessionMode.Host, false);
        var runtime = new OptionsRuntime();
        var aim = new AimAssistViewModel();
        Check(!aim.TargetTeammates, "Aim excludes teammates by default");
        Check(aim.EntityOverlayDistanceInput.Value==200, "Standalone aim retains the default 200 m display distance");
        aim.Connect(runtime,caps); aim.Enabled=true; Pump();
        Check(!Team(runtime.Last("combat.aim_assist")), "Aim enable explicitly sends false");
        Check(runtime.Last("combat.aim_assist").Arguments.GetProperty("entityOverlayDistance").GetDouble()==200, "Aim enable includes the separate display distance");
        aim.TargetTeammates=true; Pump();
        Check(Team(runtime.Last("combat.aim_assist")), "Aim checked sends true immediately");
        aim.StrengthInput.Text="-"; aim.DistanceInput.Text="999";
        aim.TargetTeammates=false; Pump();
        var command=runtime.Last("combat.aim_assist");
        Check(!Team(command) && command.Arguments.GetProperty("enabled").GetBoolean(), "Aim uncheck submits exclusion despite invalid drafts");
        Check(command.Arguments.GetProperty("strength").GetDouble()==60 && command.Arguments.GetProperty("distance").GetDouble()==35, "Aim teammate change preserves confirmed numeric settings");
        Check(aim.StrengthInput.Text=="-" && aim.DistanceInput.HasError, "Aim does not rewrite invalid drafts");
        runtime.Reject=true; aim.TargetTeammates=true; Pump();
        Check(!aim.TargetTeammates && aim.Status.Contains("失败"), "Aim rejection restores confirmed teammate selection and reports failure");
        runtime.Reject=false; aim.Disconnect();
        int before=runtime.Commands.Count; aim.TargetTeammates=true; aim.Connect(runtime,caps); Pump();
        Check(!aim.Enabled && runtime.Commands.Count==before, "Aim reconnect does not replay teammate enable intent");

        var magicRuntime=new OptionsRuntime();
        var magic=new FeatureItemViewModel(FeatureRegistry.Find("combat.silent_aim")!,magicRuntime,caps);
        Check(!magic.MagicTargetTeammates, "Magic excludes teammates by default");
        magic.Enabled=true; Pump();
        Check(magic.IsSuccessful && !Team(magicRuntime.Last("combat.silent_aim")), "Core accepts explicit false and magic enables");
        magic.MagicTargetTeammates=true; Pump();
        Check(Team(magicRuntime.Last("combat.silent_aim")), "Magic checked sends true immediately");
        magic.MagicAngleInput.Text="bad"; magic.MagicDistanceInput.Text="1001";
        magic.MagicTargetTeammates=false; Pump();
        command=magicRuntime.Last("combat.silent_aim");
        Check(!Team(command) && magic.IsSuccessful, "Magic uncheck submits exclusion despite invalid drafts");
        Check(command.Arguments.GetProperty("angle").GetDouble()==8 && command.Arguments.GetProperty("distance").GetDouble()==35, "Magic teammate change preserves confirmed numeric settings");
        Check(magic.MagicAngleInput.Text=="bad" && magic.MagicDistanceInput.HasError, "Magic does not rewrite invalid drafts");
        magicRuntime.Reject=true; magic.MagicTargetTeammates=true; Pump();
        Check(!magic.MagicTargetTeammates && magic.Status.Contains("fixture"), "Magic rejection restores confirmed teammate selection");
        magicRuntime.Reject=false; magicRuntime.Throw=true; magic.MagicTargetTeammates=true; Pump();
        Check(!magic.MagicTargetTeammates && magic.Status.Contains("失败"), "Magic exception restores confirmed teammate selection");
        magicRuntime.Throw=false; magic.Enabled=false; Pump();
        Check(!magicRuntime.Last("combat.silent_aim").Arguments.GetProperty("enabled").GetBoolean(), "Magic can disable with invalid numeric drafts");

        var store=new OptionsStore();
        var vm=new MainViewModel(preferencesStore:store,experiencePath:Path.Combine(args[0],"experience-a.json"));
        vm.AimAssist.TargetTeammates=true; var card=Magic(vm); card.MagicTargetTeammates=false;
        Check(ReferenceEquals(vm.EntityOverlayDistanceInput,vm.AimAssist.EntityOverlayDistanceInput)&&ReferenceEquals(vm.EntityOverlayDistanceInput,card.EntityOverlayDistanceInput), "Both combat owners use the one shared display-distance input");
        vm.EntityOverlayDistanceInput.Text="240.5";vm.EntityOverlayDistanceInput.Text="-";
        vm.ClosePreferences();
        var restored=new MainViewModel(preferencesStore:store,experiencePath:Path.Combine(args[0],"experience-b.json"));
        Check(restored.AimAssist.TargetTeammates && !Magic(restored).MagicTargetTeammates, "Two teammate preferences persist independently");
        Check(restored.EntityOverlayDistanceInput.Value==240.5&&TrainerUserPreferences.Parse(store.Json).General.Number("entityOverlayDistance")==240.5, "Invalid display draft persists its last valid decimal in the existing General dictionary");
        var restoredRuntime=new OptionsRuntime(); restored.Connect(restoredRuntime,caps); Pump();
        Check(!restored.AimAssist.Enabled && !Magic(restored).Enabled && !restoredRuntime.Commands.Any(c=>c.FeatureId is "combat.aim_assist" or "combat.silent_aim"), "Preference restore and connect send no combat enable command");
        card=Magic(restored); card.MagicTargetTeammates=true;
        restored.EntityOverlayDistanceInput.Text="1001";
        restored.SelectedPage=restored.Pages.First(p=>p!=restored.SelectedPage);
        Check(Magic(restored).MagicTargetTeammates, "Magic teammate preference survives page rebuild");
        card=Magic(restored);
        Check(restored.EntityOverlayDistanceInput.Text=="1001"&&ReferenceEquals(card.EntityOverlayDistanceInput,restored.EntityOverlayDistanceInput), "Page rebuilding keeps the invalid display draft and shared instance");
        int overlayBefore=restoredRuntime.Commands.Count;
        restored.ApplyEntityOverlayCommand.Execute(null);Pump();
        Check(!restored.CanApplyEntityOverlay&&restoredRuntime.Commands.Count==overlayBefore, "Invalid display draft cannot submit the independent configuration");
        restored.AimAssist.Enabled=true;card.Enabled=true;Pump();
        Check(restored.AimAssist.Enabled&&card.Enabled&&new[]{"combat.aim_assist","combat.silent_aim"}.All(id=>restoredRuntime.Last(id).Arguments.GetProperty("entityOverlayDistance").GetDouble()==240.5), "Invalid display draft does not prevent either combat owner from enabling with the last valid distance");
        restored.EntityOverlayDistanceInput.Text="320.75";restoredRuntime.OverlayReadbackDistance=320.5;
        int combatBefore=restoredRuntime.Commands.Count(c=>c.FeatureId.StartsWith("combat."));
        restored.ApplyEntityOverlayAsync().GetAwaiter().GetResult();
        Check(restoredRuntime.Last("view.entity_overlay.configure").Arguments.GetProperty("distance").GetDouble()==320.75&&restored.EntityOverlayStatus.Contains("320.5")&&restored.EntityOverlayDistanceInput.Text=="320.75", "Independent configuration sends the decimal draft, reports runtime readback and keeps the draft");
        Check(restoredRuntime.Commands.Count(c=>c.FeatureId.StartsWith("combat."))==combatBefore&&restored.AimAssist.Enabled&&card.Enabled, "Display-only apply leaves both combat toggles untouched and sends no combat request");
        restoredRuntime.Reject=true;restored.ApplyEntityOverlayAsync().GetAwaiter().GetResult();
        Check(restored.EntityOverlayStatus.Contains("失败")&&restored.CanApplyEntityOverlay&&restored.EntityOverlayDistanceInput.Text=="320.75", "Rejected display-only apply reports failure and releases the controls without changing the draft");
        restoredRuntime.Reject=false;restoredRuntime.Throw=true;restored.ApplyEntityOverlayAsync().GetAwaiter().GetResult();
        Check(restored.EntityOverlayStatus.Contains("失败")&&restored.CanApplyEntityOverlay, "A thrown display-only request reports failure and releases the controls");
        restoredRuntime.Throw=false;
        restored.AimAssist.Enabled=false;card.Enabled=false;Pump();
        restored.ClosePreferences();
        var legacy=new MainViewModel(preferencesStore:new OptionsStore(),experiencePath:Path.Combine(args[0],"experience-c.json"));
        Check(legacy.EntityOverlayDistanceInput.Value==200, "An older preference file keeps the default 200 m display distance");
        Check(!legacy.AimAssist.TargetTeammates && !Magic(legacy).MagicTargetTeammates, "Missing legacy fields default to exclude teammates"); legacy.ClosePreferences();

        {
            var preferences=new OptionsStore();var sourceRuntime=new OptionsRuntime();
            var source=new MainViewModel(preferencesStore:preferences,experiencePath:Path.Combine(args[0],"overlay-session-source.json"));
            source.Connect(sourceRuntime,caps);Pump();source.EntityOverlayDistanceInput.Text="415.5";
            source.AimAssist.Enabled=true;Magic(source).Enabled=true;Pump();source.CloseExperience();source.ClosePreferences();
            var session=new MainViewModel(preferencesStore:preferences,experiencePath:Path.Combine(args[0],"overlay-session-restore.json"));
            var sessionRuntime=new OptionsRuntime();session.Connect(sessionRuntime,caps);Pump();session.EntityOverlayDistanceInput.Text="300";
            session.RestoreLastSessionAsync().GetAwaiter().GetResult();Pump();
            Check(session.EntityOverlayDistanceInput.Value==415.5&&new[]{"combat.aim_assist","combat.silent_aim"}.All(id=>sessionRuntime.Last(id).Arguments.GetProperty("entityOverlayDistance").GetDouble()==415.5), "Explicit session restoration restores the shared distance before enabling either combat owner");
            session.PresetName="overlay-distance";session.SavePresetCommand.Execute(null);session.EntityOverlayDistanceInput.Text="600";
            session.ApplySelectedPresetAsync().GetAwaiter().GetResult();Pump();
            Check(session.EntityOverlayDistanceInput.Value==415.5&&!session.AimAssist.Enabled&&!Magic(session).Enabled, "Applying a parameter preset restores the shared distance while leaving combat features off");
            session.CloseExperience();session.ClosePreferences();
        }

        {
            var first=new OptionsRuntime{PendingOverlay=new()};
            var paging=new MainViewModel(preferencesStore:new OptionsStore(),experiencePath:Path.Combine(args[0],"overlay-reconnect.json"));
            paging.Connect(first,caps);Pump();paging.EntityOverlayDistanceInput.Text="450.5";
            var oldApply=paging.ApplyEntityOverlayAsync();
            Check(!oldApply.IsCompleted&&!paging.CanApplyEntityOverlay, "A pending display apply makes its shared button unavailable");
            paging.SuspendConnection();var next=new OptionsRuntime{PendingOverlay=new()};paging.Connect(next,caps);Pump();
            var newApply=paging.ApplyEntityOverlayAsync();paging.EntityOverlayDistanceInput.Text="bad";
            var oldData=JsonSerializer.SerializeToElement(new{distance=450.5});
            first.PendingOverlay.SetResult(new RuntimeCommandResult(true,"old",oldData,oldData,false));Pump();
            Check(oldApply.IsCompleted&&!newApply.IsCompleted&&paging.EntityOverlayStatus=="已连接 · 显示距离待应用", "An old connection reply cannot publish a display distance into the new session");
            var newData=JsonSerializer.SerializeToElement(new{distance=450.25});
            next.PendingOverlay.SetResult(new RuntimeCommandResult(true,"new",newData,newData,false));Pump();
            Check(newApply.IsCompleted&&paging.EntityOverlayStatus.Contains("450.25")&&paging.EntityOverlayDistanceInput.Text=="bad", "Only the current display reply updates confirmation and preserves a newer invalid draft");
            paging.EntityOverlayDistanceInput.Text="500";
            Check(paging.CanApplyEntityOverlay, "The current display completion releases busy state for the next valid edit");
            paging.CloseExperience();paging.ClosePreferences();
        }

        foreach (bool throws in new[]{false,true})
        {
            var delayed=new OptionsRuntime();var preferences=new OptionsStore();
            var paging=new MainViewModel(preferencesStore:preferences,experiencePath:Path.Combine(args[0],"paging-"+throws+".json"));
            paging.Connect(delayed,caps);Pump();var oldCard=Magic(paging);
            oldCard.Enabled=true;oldCard.MagicTargetTeammates=true;Pump();
            delayed.PendingMagic=new();oldCard.MagicTargetTeammates=false;
            paging.SelectedPage=paging.Pages.First(p=>p!=paging.SelectedPage);
            var currentCard=Magic(paging);currentCard.MagicDistanceInput.Text="bad";
            bool remainedBusy=currentCard.IsBusy&&!currentCard.CanInteract;
            if(throws)delayed.PendingMagic.SetException(new IOException("delayed rejection"));
            else delayed.PendingMagic.SetResult(new RuntimeCommandResult(false,"delayed rejection",null,null,false));
            delayed.PendingMagic=null;Pump();
            Check(currentCard.MagicTargetTeammates&&!currentCard.IsBusy&&currentCard.CanInteract,
                "Delayed failure restores the confirmed teammate selection on the current card: "+throws);
            Check(remainedBusy,"Page rebuild keeps pending magic operation unavailable: "+throws);
            Check(currentCard.MagicDistanceInput.Text=="bad","Rollback preserves a newer numeric draft: "+throws);
            currentCard.MagicTargetTeammates=false;Pump();
            Check(!Team(delayed.Last("combat.silent_aim")),"Unchecking after failed page transition still sends false: "+throws);
            currentCard.Enabled=false;currentCard.MagicTargetTeammates=true;Pump();
            paging.SelectedPage=paging.Pages.First(p=>p!=paging.SelectedPage);
            Check(Magic(paging).MagicTargetTeammates,"An off-state selection for the next enable survives rebuilding: "+throws);
            paging.CloseExperience();paging.ClosePreferences();
        }

        {
            var delayed=new OptionsRuntime();var preferences=new OptionsStore();
            var paging=new MainViewModel(preferencesStore:preferences,experiencePath:Path.Combine(args[0],"away.json"));
            paging.Connect(delayed,caps);Pump();var oldCard=Magic(paging);oldCard.Enabled=true;oldCard.MagicTargetTeammates=true;
            delayed.PendingMagic=new();oldCard.MagicTargetTeammates=false;
            paging.SelectedPage=paging.Pages.First(p=>p!=paging.SelectedPage);
            delayed.PendingMagic.SetResult(new RuntimeCommandResult(false,"delayed rejection",null,null,false));delayed.PendingMagic=null;Pump();
            paging.CloseExperience();paging.ClosePreferences();
            Check(TrainerUserPreferences.Parse(preferences.Json).Features["combat.silent_aim"].Boolean("targetTeammates")==true,
                "Rollback while its category is hidden updates the parameter file before close");
        }

        {
            var delayed=new OptionsRuntime();var paging=new MainViewModel(preferencesStore:new OptionsStore(),experiencePath:Path.Combine(args[0],"proof.json"));
            paging.Connect(delayed,caps);Pump();var cardBefore=Magic(paging);cardBefore.Enabled=true;cardBefore.MagicTargetTeammates=true;
            delayed.PendingProof=new();var proof=paging.RefreshRuntimeProofsAsync();
            delayed.PendingMagic=new();cardBefore.MagicTargetTeammates=false;
            var data=JsonSerializer.SerializeToElement(new[]{new{featureId="combat.silent_aim",state="applied",reason="old proof"}});
            delayed.PendingProof.SetResult(new RuntimeCommandResult(true,"old proof",data,data,false));delayed.PendingProof=null;Pump();
            paging.SelectedPage=paging.Pages.First(p=>p!=paging.SelectedPage);var pendingCard=Magic(paging);
            Check(proof.IsCompleted&&pendingCard.IsBusy&&!pendingCard.CanInteract,"A proof reply cannot finish a pending teammate operation after rebuilding");
            delayed.PendingMagic.SetResult(new RuntimeCommandResult(true,"applied",null,null,false));delayed.PendingMagic=null;Pump();
            Check(!pendingCard.IsBusy&&!pendingCard.MagicTargetTeammates&&pendingCard.CanInteract,"Only the actual operation reply releases the new card");
            paging.CloseExperience();paging.ClosePreferences();
        }

        foreach(bool throws in new[]{false,true})
        {
            var first=new OptionsRuntime();var paging=new MainViewModel(preferencesStore:new OptionsStore(),experiencePath:Path.Combine(args[0],"reconnect-"+throws+".json"));
            paging.Connect(first,caps);Pump();var firstCard=Magic(paging);firstCard.Enabled=true;firstCard.MagicTargetTeammates=true;
            paging.SuspendConnection();var replay=new TaskCompletionSource<RuntimeCommandResult>();var delayed=new OptionsRuntime{PendingMagic=replay};
            paging.Connect(delayed,caps);Pump();var currentCard=Magic(paging);
            delayed.PendingMagic=new();currentCard.MagicTargetTeammates=false;
            if(throws)replay.SetException(new IOException("old reconnect failure"));
            else replay.SetResult(new RuntimeCommandResult(true,"old reconnect reply",null,null,false));Pump();
            paging.SelectedPage=paging.Pages.First(p=>p!=paging.SelectedPage);currentCard=Magic(paging);
            Check(currentCard.IsBusy&&!currentCard.CanInteract,"An older reconnect reply cannot release a newer teammate request: "+throws);
            delayed.PendingMagic.SetResult(new RuntimeCommandResult(true,"applied",null,null,false));delayed.PendingMagic=null;Pump();
            Check(currentCard.Enabled&&!currentCard.IsBusy&&!currentCard.MagicTargetTeammates,"The latest teammate result survives an older reconnect reply: "+throws);
            paging.CloseExperience();paging.ClosePreferences();
        }

        double radius; bool shortForm;
        Check(MagicRangePolicy.Project(30,90,2000,1000,out radius,out shortForm) && Math.Abs(radius-288.6751346)<.0001 && !shortForm, "Circle projection matches perspective half-angle math");
        MagicRangePolicy.Project(8,60,1920,1080,out double hip,out _); MagicRangePolicy.Project(8,30,1920,1080,out double ads,out _);
        Check(ads>hip*2 && ads<hip*2.2, "ADS narrower FOV expands the projected circle");
        Check(MagicRangePolicy.Project(180,60,1920,1080,out radius,out shortForm)&&shortForm&&radius==1080*.46, "Full-sphere angle uses explicit abbreviated view");
        Check(!MagicRangePolicy.Project(double.NaN,60,1920,1080,out _,out _)&&!MagicRangePolicy.Project(8,0,1920,1080,out _,out _)&&!MagicRangePolicy.Project(8,60,0,1080,out _,out _), "Invalid camera inputs hide range drawing");
        File.WriteAllText(Path.Combine(args[0],"results.json"),JsonSerializer.Serialize(new{passed=true,checks=Checks.Count,tests=Checks,fixtureOnly=true,gameInjected=false,
            applicationSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Target options verification: "+Checks.Count+" checks passed"); return 0;
    }
}
