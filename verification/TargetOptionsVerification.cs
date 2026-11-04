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
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken token)
    {
        Commands.Add(command);
        if (command.FeatureId == "combat.silent_aim" && PendingMagic != null) return new(PendingMagic.Task);
        if (command.FeatureId == "features.proofs" && PendingProof != null) return new(PendingProof.Task);
        if (Throw && command.FeatureId.StartsWith("combat.")) throw new IOException("fixture disconnected");
        object data = command.FeatureId switch {
            "catalog.items" => new { items = Array.Empty<object>() },
            "catalog.islands" => new { islands = Array.Empty<object>() },
            "features.proofs" => new { features = Array.Empty<object>() },
            _ => new { applied = true }
        };
        var json = JsonSerializer.SerializeToElement(data);
        return ValueTask.FromResult(new RuntimeCommandResult(!Reject || !command.FeatureId.StartsWith("combat."), "fixture", json, json, false));
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
        aim.Connect(runtime,caps); aim.Enabled=true; Pump();
        Check(!Team(runtime.Last("combat.aim_assist")), "Aim enable explicitly sends false");
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
        vm.ClosePreferences();
        var restored=new MainViewModel(preferencesStore:store,experiencePath:Path.Combine(args[0],"experience-b.json"));
        Check(restored.AimAssist.TargetTeammates && !Magic(restored).MagicTargetTeammates, "Two teammate preferences persist independently");
        var restoredRuntime=new OptionsRuntime(); restored.Connect(restoredRuntime,caps); Pump();
        Check(!restored.AimAssist.Enabled && !Magic(restored).Enabled && !restoredRuntime.Commands.Any(c=>c.FeatureId is "combat.aim_assist" or "combat.silent_aim"), "Preference restore and connect send no combat enable command");
        card=Magic(restored); card.MagicTargetTeammates=true;
        restored.SelectedPage=restored.Pages.First(p=>p!=restored.SelectedPage);
        Check(Magic(restored).MagicTargetTeammates, "Magic teammate preference survives page rebuild");
        restored.ClosePreferences();
        var legacy=new MainViewModel(preferencesStore:new OptionsStore(),experiencePath:Path.Combine(args[0],"experience-c.json"));
        Check(!legacy.AimAssist.TargetTeammates && !Magic(legacy).MagicTargetTeammates, "Missing legacy fields default to exclude teammates"); legacy.ClosePreferences();

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
        File.WriteAllText(Path.Combine(args[0],"results.json"),JsonSerializer.Serialize(new{passed=true,checks=Checks.Count,tests=Checks,fixtureOnly=true,gameInjected=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Target options verification: "+Checks.Count+" checks passed"); return 0;
    }
}
