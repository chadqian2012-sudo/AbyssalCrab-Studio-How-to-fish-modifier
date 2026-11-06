using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HowToFishTrainer.App;
using HowToFishTrainer.Core;

sealed class OverlayFixtureRuntime : IGameRuntimeClient
{
    public readonly List<FeatureCommand> Commands = new();
    public string? HeldCommand;
    public readonly Queue<TaskCompletionSource<RuntimeCommandResult>> Pending = new();
    public int CatalogRevision;
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken token)
    {
        Commands.Add(command);
        if (command.FeatureId == HeldCommand)
        {
            var completion = new TaskCompletionSource<RuntimeCommandResult>(); Pending.Enqueue(completion); return new(completion.Task);
        }
        object data = command.FeatureId switch {
            "catalog.items" => new { items = new object[] {
                new { id = 11, name = "Rifle " + CatalogRevision, fish = false, kind = "枪械", weapon = new { damage = 9 }, attachments = new { sights = new[] { new { id = "scope", name = "Scope" } }, extendedMag = true, laser = true }, skins = new[] { new { id = "blue", name = "Blue", rarity = "rare" } } },
                new { id = 5, name = "Apple", fish = false, kind = "食品" }, new { id = 21, name = "Fish", fish = true, kind = "鱼类" } } },
            "catalog.islands" => new { islands = Array.Empty<object>() },
            "players.catalog" => new { players = Array.Empty<object>() },
            "features.proofs" => Array.Empty<object>(),
            "fun.event.catalog" => new { events = FunEventDefinitions.All.Select(e => new { id = e.Id, available = true, reason = "fixture" }).ToArray(), totalFunEntities = 0, currentWaveCount = 0 },
            "casino.status" => new { available = true, outcomeMode = "black", multiplier = 2, remainingRounds = 0 },
            "world.position.read" => new { x = 3.0, y = 4.0, z = 5.0 },
            _ => new { applied = true, x = 10.0, y = 20.0, z = 30.0 }
        };
        return new(Result(data));
    }
    public static RuntimeCommandResult Result(object? data = null)
    {
        var value = JsonSerializer.SerializeToElement(data ?? new { applied = true, x = 3.0, y = 4.0, z = 5.0 });
        return new(true, "fixture", value, value, false);
    }
}
sealed class OverlayFixtureStore : IUserPreferencesStore
{
    public TrainerUserPreferences Value = new();
    public string? Warning => null;
    public TrainerUserPreferences Load() => Value.Copy();
    public void Save(TrainerUserPreferences preferences) => Value = preferences.Copy();
}
sealed class SilentToggleSound : IToggleSoundPlayer { public void PlayEnabled() { } }

static class OverlayVerification
{
    static readonly List<string> Passed = new(), Failed = new();
    static string Root = "";
    static void Check(bool condition, string name) => (condition ? Passed : Failed).Add(name);
    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    static void Await(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("Fixture task did not finish");
        task.GetAwaiter().GetResult(); Pump();
    }
    static MainViewModel Create(OverlayFixtureRuntime runtime, out OverlayFixtureStore store, out string path)
    {
        path = Path.Combine(Root, Guid.NewGuid().ToString("N"), "experience.json");
        store = new OverlayFixtureStore();
        var vm = new MainViewModel(new SilentToggleSound(), preferencesStore: store, experiencePath: path);
        vm.Connect(runtime, CapabilityMatrix.For(SessionMode.Host, false)); Pump();
        return vm;
    }
    static FeatureItemViewModel Feature(MainViewModel vm, string id) => vm.AllFeatures.Single(f => f.Descriptor.Id == id);
    static void Select(MainViewModel vm, string category) => vm.SelectedPage = vm.Pages.Single(p => p.Category == category);
    static void Close(MainViewModel vm) { vm.Disconnect(); vm.ClosePreferences(); vm.CloseExperience(); Pump(); }
    static OverlayShortcutViewModel Pinned(MainViewModel vm, string title) => vm.SelectedOverlayPanel!.Items.Single(i => i.Title == title);
    static TrainerTeleportPoint AddPoint(MainViewModel vm, string name = "Camp")
    {
        var point = new TrainerTeleportPoint("Exploration", name, 10, 20, 30); vm.TeleportPoints.Add(point); return point;
    }

    [STAThread] static int Main(string[] args)
    {
        Root = Path.GetFullPath(args[0]); Directory.CreateDirectory(Root);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { SharedFeatures(); PanelsAndPersistence(); TeleportBoundaries(); PresetAndEmergency(); SettingsMigration(); }
        catch (Exception e) { Failed.Add("Unhandled: " + e); }
        File.WriteAllText(Path.Combine(Root, "results.json"), JsonSerializer.Serialize(new {
            passed = Failed.Count == 0, checks = Passed.Count + Failed.Count, passedChecks = Passed, failedChecks = Failed,
            fixtureOnly = true, gameInjected = false, uiVerified = false,
            applicationSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))).ToLowerInvariant()
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Overlay: {Passed.Count} passed, {Failed.Count} failed");
        foreach (var failed in Failed) Console.WriteLine("FAILED: " + failed);
        return Failed.Count == 0 ? 0 : 1;
    }

    static void SharedFeatures()
    {
        var runtime = new OverlayFixtureRuntime(); var vm = Create(runtime, out var store, out _);
        var move = Feature(vm, "player.move.multiplier"); var spin = Feature(vm, "player.spin.lock");
        move.ValueInput.Text = "2.5"; move.ValueInput.Text = "-"; spin.SpinSpeedText = "7.25";
        Select(vm, "钓鱼"); Select(vm, "玩家");
        Check(ReferenceEquals(move, Feature(vm, move.Descriptor.Id)) && vm.Features.Contains(move), "Category changes reuse the existing movement object");
        Check(move.ValueInput.Text == "-" && move.ValueInput.HasError, "Incomplete numeric draft survives page changes");
        vm.AddFeatureToOverlayCommand.Execute(move);
        Check(ReferenceEquals(vm.SelectedOverlayPanel!.Items.Single().Feature, move), "Pinned numeric feature and main page share exactly one instance");
        Select(vm, "钓鱼"); vm.FlushPreferences();
        Check(store.Value.Features[move.Descriptor.Id].Number("value") == 2.5 && store.Value.Features[spin.Descriptor.Id].Number("spinSpeed") == 7.25,
            "Persistence captures offscreen feature last-valid values");
        var item = Feature(vm, "generation.item"); item.SelectedOption = item.Options.Single(p => p.Id == 11);
        item.SelectedSight = item.SelectedOption.Sights.Single(); item.QuantityInput.Text = "3"; item.QuantityInput.Text = "-";
        runtime.CatalogRevision++; vm.RefreshCatalogCommand.Execute(null); Pump();
        Check(ReferenceEquals(item, Feature(vm, "generation.item")) && ReferenceEquals(move, Feature(vm, move.Descriptor.Id)), "Catalog refresh keeps generator and ordinary feature instances");
        Check(item.SelectedOption?.Id == 11 && item.SelectedOption.Name.EndsWith("1") && item.SelectedSight?.Id == "scope" && item.QuantityInput.Text == "-",
            "Catalog refresh remaps selected IDs and preserves unfinished generator drafts");
        Check(!item.CanGenerate || item.QuantityInput.HasError, "Invalid generator quantity remains invalid after catalog refresh");
        int before = runtime.Commands.Count; item.ExecuteCommand.Execute(null); Pump();
        Check(!runtime.Commands.Skip(before).Any(c => c.FeatureId == "generation.item"), "Invalid generator draft cannot execute after catalog refresh");
        Close(vm);
    }

    static void PanelsAndPersistence()
    {
        var runtime = new OverlayFixtureRuntime(); var vm = Create(runtime, out var store, out string path);
        Check(vm.OverlayPanels.Count == 1 && vm.SelectedOverlayPanel?.Name == "常用" && vm.SelectedOverlayPanel.Items.Count == 0, "First launch has one empty common panel");
        var first = vm.SelectedOverlayPanel!; var move = Feature(vm, "player.move.multiplier");
        vm.AddFeatureToOverlayCommand.Execute(move); vm.AddFeatureToOverlayCommand.Execute(move);
        Check(first.Items.Count == 1, "Duplicate pin does not add a second copy");
        var point = AddPoint(vm); vm.AddTeleportToOverlayCommand.Execute(point);
        vm.PresetName = "Explore"; vm.SavePresetCommand.Execute(null); vm.AddPresetToOverlayCommand.Execute(vm.SelectedPreset);
        Check(first.Items.Count == 3 && first.Shortcuts.Select(i => i.Kind).SequenceEqual(new[] { "feature", "teleport", "preset" }), "Mixed shortcuts retain explicit feature, teleport and preset types");
        first.Items[2].MoveUpCommand.Execute(null);
        Check(first.Shortcuts.Select(i => i.Kind).SequenceEqual(new[] { "feature", "preset", "teleport" }), "Move-up reorders only the selected panel");
        first.Items[1].MoveDownCommand.Execute(null); first.Items[0].MoveUpCommand.Execute(null);
        Check(first.Shortcuts.Select(i => i.Kind).SequenceEqual(new[] { "feature", "teleport", "preset" }), "Move-down and boundary moves preserve the remaining order");
        vm.OverlayPanelName = "Battle"; vm.CreateOverlayPanelCommand.Execute(null);
        Check(vm.OverlayPanels.Count == 2 && vm.SelectedOverlayPanel!.Items.Count == 0 && first.Items.Count == 3, "Creating another panel does not copy or erase existing pins");
        vm.OverlayPanelName = "Battle 2"; vm.RenameOverlayPanelCommand.Execute(null);
        Check(vm.SelectedOverlayPanel?.Name == "Battle 2", "Panel rename preserves its identity");
        vm.SelectedOverlayPanel = first; vm.WidgetWidth = 750; vm.WidgetHeight = 500; vm.OverlayHotkey = "F8"; vm.FlushExperience();
        var stored = ExperienceSettingsStore.Parse(File.ReadAllText(path));
        Check(stored.Version == 2 && stored.OverlayPanels.Count == 2 && stored.OverlayPanels[0].Shortcuts.Count == 3 && stored.SelectedOverlayPanelId == first.Id,
            "Stored experience v2 retains all profiles, ordered pins and selected panel");
        Check(!File.ReadAllText(path).Contains("ExecuteCommand") && !File.ReadAllText(path).Contains("\"Items\""), "Persistent profiles contain references rather than live feature objects or commands");
        var restarted = new MainViewModel(new SilentToggleSound(), preferencesStore: store, experiencePath: path);
        Check(restarted.SelectedOverlayPanel?.Id == first.Id && restarted.SelectedOverlayPanel.Items.Count == 3 && restarted.OverlayHotkey == "F8" && restarted.WidgetWidth == 750 && restarted.WidgetHeight == 500,
            "Restart restores chosen panel, key and expanded dimensions");
        Check(restarted.SelectedOverlayPanel!.Items.Single(i => i.KindLabel == "传送自己").IsAvailable == false,
            "A reference to an absent saved point is disabled rather than rebound to another point");
        Close(restarted);
        vm.SelectedTeleportPoint = point; vm.PointName = "Harbor"; vm.SavePointCommand.Execute(null);
        Check(first.Shortcuts.Any(i => i.Kind == "teleport" && i.Key == "Harbor") && !first.Shortcuts.Any(i => i.Key == "Camp"), "Teleport rename updates every pinned reference");
        vm.SelectedPreset = vm.Presets.Single(); vm.PresetName = "Explore renamed"; vm.RenamePresetCommand.Execute(null);
        Check(first.Shortcuts.Any(i => i.Kind == "preset" && i.Key == "Explore renamed"), "Preset rename updates pinned references");
        vm.DeletePresetCommand.Execute(null); vm.DeletePointCommand.Execute(null);
        Check(first.Items.Count == 1, "Deleting a preset and point removes only their pinned references");
        vm.PresetName = "ABC"; vm.SavePresetCommand.Execute(null); vm.AddPresetToOverlayCommand.Execute(vm.SelectedPreset);
        vm.PresetName = "abc"; vm.SavePresetCommand.Execute(null);
        Check(vm.Presets.Count == 1 && vm.SelectedPreset?.Name == "abc" && first.Shortcuts.Single(i => i.Kind == "preset").Key == "abc" && first.Items.Single(i => i.KindLabel == "载入参数").IsAvailable,
            "Case-only preset overwrite updates the pinned name without creating a duplicate or missing reference");
        vm.DeletePresetCommand.Execute(null);
        vm.SelectedOverlayPanel = vm.OverlayPanels.Last(); vm.DeleteOverlayPanelCommand.Execute(null); vm.DeleteOverlayPanelCommand.Execute(null);
        Check(vm.OverlayPanels.Count == 1 && vm.SelectedOverlayPanel == first && first.Items.Count == 1, "Deleting profiles preserves the final panel and its entries");
        first.Items.Single().RemoveCommand.Execute(null); Check(first.Items.Count == 0, "Last shortcut can be removed without deleting the panel");
        Close(vm);
    }

    static void TeleportBoundaries()
    {
        var runtime = new OverlayFixtureRuntime(); var vm = Create(runtime, out _, out _); var point = AddPoint(vm);
        vm.RoomMembersPage.SelectedMember = new("remote-42", "Teammate", "42", false, false, true, 100, 100, 0, 0, 90, 90, 90);
        vm.TargetRoomMember = true; vm.CurrentXInput.Text = "77.5"; vm.CurrentYInput.Text = "-"; vm.CurrentZInput.Text = "13";
        int sent = runtime.Commands.Count; Await(vm.TeleportSavedPointAsync(point.Group, point.Name));
        var commands = runtime.Commands.Skip(sent).ToArray();
        Check(commands.Any(c => c.FeatureId == "world.position.read") && commands.Count(c => c.FeatureId == "world.teleport") == 1 && commands.All(c => !c.FeatureId.StartsWith("players.")),
            "Shortcut teleport ignores member selection and only targets the local player");
        var teleport = commands.Single(c => c.FeatureId == "world.teleport");
        Check(teleport.Arguments.GetProperty("x").GetDouble() == 10 && teleport.Arguments.GetProperty("y").GetDouble() == 20 && teleport.Arguments.GetProperty("z").GetDouble() == 30 && teleport.Arguments.GetProperty("safe").GetBoolean(),
            "Shortcut teleport uses saved coordinates and existing safe-landing setting");
        Check(vm.TargetRoomMember && vm.CurrentXInput.Text == "77.5" && vm.CurrentYInput.Text == "-" && vm.CurrentZInput.Text == "13",
            "Shortcut execution preserves the main editor's target and unfinished drafts");
        runtime.HeldCommand = "world.position.read"; sent = runtime.Commands.Count;
        var first = vm.TeleportSavedPointAsync(point.Group, point.Name); var duplicate = vm.TeleportSavedPointAsync(point.Group, point.Name);
        Check(!first.IsCompleted && duplicate.IsCompleted && runtime.Pending.Count == 1 && runtime.Commands.Skip(sent).Count(c => c.FeatureId == "world.position.read") == 1,
            "Rapid double click cannot dispatch overlapping teleports");
        runtime.HeldCommand = null; runtime.Pending.Dequeue().SetResult(OverlayFixtureRuntime.Result()); Await(first);
        Check(runtime.Commands.Skip(sent).Count(c => c.FeatureId == "world.teleport") == 1 && vm.CanPositionAction, "Single pending teleport completes and releases busy state");
        runtime.HeldCommand = "world.position.read"; sent = runtime.Commands.Count;
        vm.OverlayStatus = "Previous success";
        var failed = vm.TeleportSavedPointAsync(point.Group, point.Name);
        runtime.Pending.Dequeue().SetException(new IOException("fixture-read-error")); Await(failed);
        Check(vm.OverlayStatus == vm.PositionStatus && vm.OverlayStatus.Contains("fixture-read-error") && vm.CanPositionAction
            && !runtime.Commands.Skip(sent).Any(c => c.FeatureId == "world.teleport"),
            "Failed shortcut teleport replaces prior success in the overlay and releases busy state");
        runtime.HeldCommand = "world.position.read"; var stale = vm.TeleportSavedPointAsync(point.Group, point.Name);
        vm.Disconnect(); var current = new OverlayFixtureRuntime(); vm.Connect(current, CapabilityMatrix.For(SessionMode.Host, false)); Pump();
        int oldSent = runtime.Commands.Count, newSent = current.Commands.Count; vm.OverlayStatus = "Current world";
        runtime.Pending.Dequeue().SetResult(OverlayFixtureRuntime.Result()); Await(stale);
        Check(runtime.Commands.Count == oldSent && current.Commands.Count == newSent && vm.OverlayStatus == "Current world" && vm.CanPositionAction,
            "Old-world position response cannot teleport, overwrite status or hold the current controls busy");
        sent = current.Commands.Count; Await(vm.TeleportSavedPointAsync("missing", "missing"));
        Check(current.Commands.Count == sent, "Missing shortcut point sends no runtime commands"); Close(vm);
    }

    static void PresetAndEmergency()
    {
        var runtime = new OverlayFixtureRuntime(); var vm = Create(runtime, out _, out _);
        var move = Feature(vm, "player.move.multiplier"); move.ValueInput.Text = "2.5";
        vm.PresetName = "Travel"; vm.SavePresetCommand.Execute(null); move.ValueInput.Text = "7";
        int sent = runtime.Commands.Count; Await(vm.LoadOverlayPresetAsync("Travel"));
        var commands = runtime.Commands.Skip(sent).ToArray();
        Check(Feature(vm, move.Descriptor.Id).ValueInput.Value == 2.5 && vm.ActivePresetName == "Travel", "Overlay preset restores named parameter values");
        Check(commands.Count(c => c.FeatureId == "locks.disable_all") == 1 && !commands.Any(c => c.FeatureId is "world.teleport" or "generation.item" or "generation.fish" || c.FeatureId.StartsWith("money.") || c.FeatureId.EndsWith(".multiplier") || c.FeatureId.EndsWith(".lock")),
            "Loading a parameter preset only clears locks and never replays numeric, toggle or one-shot actions");
        var health = Feature(vm, "player.health.lock"); health.Enabled = true; Pump(); Select(vm, "钓鱼");
        Check(health.Enabled && health.IsSuccessful && !vm.Features.Contains(health), "Emergency test begins with an active offscreen function");
        Await(vm.EmergencyDisableAsync());
        Check(!Feature(vm, "player.health.lock").Enabled && vm.AllFeatures.Where(f => f.Descriptor.Control == FeatureControl.Toggle).All(f => !f.Enabled),
            "Emergency stop clears shared offscreen switches as well as the current page");
        runtime.HeldCommand = "player.health.lock"; health = Feature(vm, "player.health.lock"); health.Enabled = true; Pump();
        Check(health.IsBusy && runtime.Pending.Count == 1, "Late-toggle test reaches an in-flight request");
        Await(vm.EmergencyDisableAsync()); var afterStop = Feature(vm, "player.health.lock");
        runtime.Pending.Dequeue().SetResult(OverlayFixtureRuntime.Result()); Pump();
        Check(!afterStop.Enabled && !afterStop.IsBusy && afterStop.CanInteract && ReferenceEquals(afterStop, Feature(vm, "player.health.lock")),
            "A delayed pre-stop toggle response cannot reopen the shared function");
        runtime.HeldCommand = "locks.disable_all"; var preset = vm.LoadOverlayPresetAsync("Travel");
        vm.ResetWorldContext(); vm.OverlayStatus = "New world";
        runtime.Pending.Dequeue().SetResult(OverlayFixtureRuntime.Result()); Await(preset);
        Check(vm.OverlayStatus == "New world", "Late preset wrapper does not overwrite current-world overlay feedback");
        Close(vm);
    }

    static void SettingsMigration()
    {
        var values = new ParameterValues(); values.Put("quantity", 3.0);
        var legacy = new ExperienceSettings { Version = 1, WidgetVisible = true, WidgetLeft = 17, WidgetTop = 29, CompactNavigation = true, PointSort = "名称", SpinCurve = "脉冲", RefillLimit = 4 };
        legacy.Presets.Add(new("Legacy preset", "{}")); legacy.Favorites.Add(new("generation.item", 11, "Rifle", values)); legacy.Recent.Add(new("generation.fish", 21, "Fish", values));
        legacy.Searches.Add("Crab"); legacy.Points.Add(new("Group", "Name", "紫色", "★", "note", true)); legacy.AnimalCounts["seagull-migration"] = 24;
        var migrated = ExperienceSettingsStore.Parse(JsonSerializer.Serialize(legacy));
        Check(migrated.Version == 2 && migrated.WidgetVisible && migrated.WidgetLeft == 17 && migrated.WidgetTop == 29 && migrated.CompactNavigation && migrated.PointSort == "名称" && migrated.SpinCurve == "脉冲" && migrated.RefillLimit == 4,
            "v1 migration retains existing scalar experience preferences");
        Check(migrated.Presets.Single().Name == "Legacy preset" && migrated.Favorites.Single().Parameters.Number("quantity") == 3 && migrated.Recent.Single().Id == 21 && migrated.Searches.Single() == "Crab" && migrated.Points.Single().Note == "note" && migrated.AnimalCounts["seagull-migration"] == 24,
            "v1 migration retains presets, bookmarks, recent entries, searches, point metadata and animal counts");
        var parsed = ExperienceSettingsStore.Parse("{\"Version\":2,\"WidgetWidth\":-1,\"WidgetHeight\":99999,\"OverlayHotkey\":\"\",\"OverlayPanels\":[null,{\"Id\":\"p\",\"Name\":\"Valid\",\"Shortcuts\":[null,{\"Kind\":\"feature\",\"Key\":\"player.health.lock\"},{\"Kind\":\"feature\",\"Key\":\"player.health.lock\"},{\"Kind\":\"bad\",\"Key\":\"x\"},{\"Kind\":\"preset\",\"Key\":\" \"}]}]}");
        Check(parsed.WidgetWidth == 620 && parsed.WidgetHeight == 680 && parsed.OverlayHotkey == "Home" && parsed.OverlayPanels.Single().Shortcuts.Count == 1,
            "Invalid optional overlay fields fall back without losing the valid panel and shortcut");
        string path = Path.Combine(Root, "future-settings.json"); const string future = "{\"Version\":3,\"FutureData\":123}"; File.WriteAllText(path, future);
        var futureStore = new ExperienceSettingsStore(path); futureStore.Load(); bool rejected = false;
        try { futureStore.Save(new()); } catch (IOException) { rejected = true; }
        Check(rejected && File.ReadAllText(path) == future, "Future experience versions remain protected from overwrite");
    }
}
