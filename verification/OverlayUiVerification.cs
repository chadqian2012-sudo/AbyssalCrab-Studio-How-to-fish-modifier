using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HowToFishTrainer.App;
using HowToFishTrainer.Core;

sealed class OverlayUiPreferenceStore : IUserPreferencesStore
{
    public string? Warning => null;
    public TrainerUserPreferences Load() => TrainerUserPreferences.Parse("{}");
    public void Save(TrainerUserPreferences value) { }
}

sealed class OverlayUiRuntime : IGameRuntimeClient
{
    public readonly List<string> Calls = new();
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken token)
    {
        Calls.Add(command.FeatureId);
        object result = new { applied = true };
        if (command.FeatureId == "catalog.items") result = new { items = new object[] {
            new { id = 11, name = "测试步枪", fish = false, kind = "枪械", weapon = new { damage = 9 }, attachments = new { sights = new[] { new { id = "scope", name = "瞄准镜" } }, barrels = Array.Empty<object>(), ammo = Array.Empty<object>(), extendedMag = true, laser = true }, skins = Array.Empty<object>() },
            new { id = 21, name = "测试鱼", fish = true, kind = "鱼类" } } };
        if (command.FeatureId == "catalog.islands") result = new { islands = new[] { new { index = 1, key = "island:test", name = "测试岛", rawName = "Test", available = true, runtimeIndex = 1, sceneBuildIndex = 2 } } };
        if (command.FeatureId == "players.catalog") result = new { players = Array.Empty<object>() };
        if (command.FeatureId == "features.proofs") result = Array.Empty<object>();
        if (command.FeatureId == "fun.event.catalog") result = new { events = FunEventDefinitions.All.Select(e => new { id = e.Id, available = true, reason = "fixture" }).ToArray(), totalFunEntities = 0, currentWaveCount = 0 };
        if (command.FeatureId == "casino.status") result = new { available = true, outcomeMode = "black", multiplier = 2, remainingRounds = 0 };
        var json = JsonSerializer.SerializeToElement(result);
        return ValueTask.FromResult(new RuntimeCommandResult(true, "离线界面样例", json, json, false));
    }
}

static class OverlayUiVerification
{
    private static readonly List<string> Checks = new();
    private static readonly List<string> Images = new();
    private static void Check(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); Checks.Add(name); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Render(FrameworkElement root, string path, int width = 1000, int height = 720)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); Pump();
        var settled = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); settled.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(settled); root.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file); Images.Add(path);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        if (!Environment.GetCommandLineArgs().Contains("--ui-preview")) throw new InvalidOperationException("Must run with --ui-preview");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = (ResourceDictionary)Application.LoadComponent(new Uri("/HowToFishTrainer;component/themes/theme.xaml", UriKind.Relative));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var errors = new StringWriter(); var listener = new TextWriterTraceListener(errors);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var vm = new MainViewModel(preferencesStore: new OverlayUiPreferenceStore(), experiencePath: Path.Combine(output, "isolated", Guid.NewGuid().ToString("N"), "experience.json"));
        var runtime = new OverlayUiRuntime(); vm.Connect(runtime, CapabilityMatrix.For(SessionMode.Host, false)); Pump();
        vm.OverlayPanelName = "探索与战斗"; vm.CreateOverlayPanelCommand.Execute(null);
        foreach (string id in new[] { "player.move.multiplier", "player.health.lock", "combat.silent_aim" })
        {
            var choice = vm.OverlayFeatureChoices.FirstOrDefault(x => x.Id == id);
            if (choice != null) vm.AddFeatureToOverlayCommand.Execute(choice);
        }
        var point = new TrainerTeleportPoint("自定义", "营地", 1, 2, 3); vm.TeleportPoints.Add(point); vm.AddTeleportToOverlayCommand.Execute(point);
        vm.PresetName = "探索参数"; vm.SavePresetCommand.Execute(null); vm.AddPresetToOverlayCommand.Execute(vm.SelectedPreset);
        var main = new Window { DataContext = vm };
        var secondView = new TrainerContentView { DataContext = vm }; main.Content = secondView;
        using var overlay = new StatusWidget(vm, main, () => null);
        var surface = (FrameworkElement)overlay.FindName("ExpandedSurface"); surface.Visibility = Visibility.Visible;
        ((FrameworkElement)overlay.FindName("Launcher")).Visibility = Visibility.Collapsed;
        var tabs = (TabControl)overlay.FindName("OverlayTabs");
        var content = (FrameworkElement)overlay.Content;
        int callsBeforeRender = runtime.Calls.Count;
        for (int i = 0; i < tabs.Items.Count; i++) { tabs.SelectedIndex = i; Render(content, Path.Combine(output, "overlay-tab-" + i + ".png")); }
        Check(tabs.Items.Count == 5, "Five overlay tabs construct and render");
        tabs.SelectedIndex = 1;
        for (int i = 0; i < vm.Pages.Count; i++) { vm.SelectedPage = vm.Pages[i]; Render(content, Path.Combine(output, "all-page-" + i + ".png")); }
        Check(vm.Pages.Count == 11, "All eleven dedicated and generic pages render from shared view");
        vm.SelectedPage = vm.Pages.First(p => p.Category == "玩家");
        var feature = vm.AllFeatures.First(f => f.Descriptor.Id == "player.move.multiplier");
        feature.ValueInput.Text = "2.75";
        Render(content, Path.Combine(output, "shared-overlay-player.png"));
        Render(secondView, Path.Combine(output, "shared-main-player.png"), 1400, 800);
        var overlayView = (TrainerContentView)overlay.FindName("TrainerContent");
        Check(Find<NumericInputBox>(overlayView).Any(n => ReferenceEquals(n.Input, feature.ValueInput)), "Overlay uses the shared numeric input owner");
        Check(Find<NumericInputBox>(secondView).Any(n => ReferenceEquals(n.Input, feature.ValueInput)), "Main page uses the same numeric input owner");
        Check(feature.ValueInput.Text == "2.75", "Rendering both hosts preserves numeric draft");
        foreach (int tab in new[] { 0, 1, 4 }) { tabs.SelectedIndex = tab; Render(content, Path.Combine(output, "default-620-tab-" + tab + ".png"), 620, 680); }
        tabs.SelectedIndex = 1;
        vm.SelectedPage = vm.Pages.First(p => p.Category == "鱼类生成");
        Render(content, Path.Combine(output, "fish-overlay.png"));
        Render(secondView, Path.Combine(output, "fish-main.png"), 1400, 800);
        var fishRadio = Find<RadioButton>(overlayView).First(r => Equals(r.Content, "死鱼"));
        fishRadio.IsChecked = true; Pump();
        var fishOwner = (FeatureItemViewModel)fishRadio.DataContext;
        Check(fishOwner.DeadFish && !fishOwner.AliveFish && Find<RadioButton>(secondView).First(r => Equals(r.Content, "死鱼")).IsChecked == true, "Fish selection is synchronized without cross-view radio grouping");
        var writes = runtime.Calls.Skip(callsBeforeRender).Where(id => !new[] { "players.catalog", "players.inspect", "catalog.items", "catalog.islands", "features.proofs", "fun.event.catalog", "fun.event.availability", "casino.status" }.Contains(id)).ToArray();
        Check(writes.Length == 0, "Building and navigating overlay sends no gameplay write commands");
        var nativeKeys = typeof(StatusWidget).Assembly.GetType("HowToFishTrainer.App.NativeHotkeys")!;
        var parse = nativeKeys.GetMethod("TryParse", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Parses(string key) { object[] parameters = { key, 0u, 0u }; return (bool)parse.Invoke(null, parameters)!; }
        Check(Parses("Home") && Parses("F8") && Parses("Ctrl+Home"), "Supported hotkey strings parse");
        Check(!Parses("Ctrl+Shift+F12") && !Parses("Ctrl+Ctrl+Home") && !Parses("Escape") && !Parses("Ctrl++Home") && !Parses("74"), "Reserved and invalid hotkeys are rejected");
        listener.Flush(); string bindingErrors = errors.ToString(); File.WriteAllText(Path.Combine(output, "binding-errors.txt"), bindingErrors);
        Check(string.IsNullOrWhiteSpace(bindingErrors), "No WPF binding errors during all overlay and feature-page rendering");
        vm.Disconnect(); vm.CloseExperience(); vm.ClosePreferences(); main.Close();
        File.WriteAllText(Path.Combine(output, "ui-verification.json"), JsonSerializer.Serialize(new { verificationVersion = "20261105-overlay-ui-v2", appAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(StatusWidget).Assembly.Location))), runnerAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(OverlayUiVerification).Assembly.Location))), checks = Checks, images = Images, realGameOpened = false, desktopInputSent = false, formalPreferencesUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS " + Checks.Count + " UI checks; " + Images.Count + " rendered images");
        return 0;
    }
}
