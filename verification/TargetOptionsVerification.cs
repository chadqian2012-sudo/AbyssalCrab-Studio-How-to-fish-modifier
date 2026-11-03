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
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken token)
    {
        Commands.Add(command);
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
