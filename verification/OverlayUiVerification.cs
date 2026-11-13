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
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HowToFishTrainer.App;
using HowToFishTrainer.Core;

sealed class OverlayUiPreferenceStore : IUserPreferencesStore
{
    public string? Warning => null;
    public TrainerUserPreferences? Saved { get; private set; }
    public int SaveCount { get; private set; }
    public TrainerUserPreferences Load() => TrainerUserPreferences.Parse("{}");
    public void Save(TrainerUserPreferences value) { Saved = value.Copy(); SaveCount++; }
}

sealed class OverlayUiRuntime : IGameRuntimeClient
{
    public readonly List<string> Calls = new();
    public TaskCompletionSource<RuntimeCommandResult>? PendingClear;
    public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken token)
    {
        Calls.Add(command.FeatureId);
        if (command.FeatureId == "locks.disable_all" && PendingClear != null) return new(PendingClear.Task);
        object result = new { applied = true };
        if (command.FeatureId == "catalog.items") result = new { items = new object[] {
            new { id = 11, name = "测试步枪", fish = false, kind = "枪械", weapon = new { damage = 9 }, attachments = new { sights = new[] { new { id = "scope", name = "瞄准镜" } }, barrels = Array.Empty<object>(), ammo = Array.Empty<object>(), extendedMag = true, laser = true }, skins = Array.Empty<object>() },
            new { id = 21, name = "测试鱼", fish = true, kind = "鱼类" } }.Concat(Enumerable.Range(0, 20).Select(i => (object)new { id = 100 + i, name = "排版目录" + i, fish = false, kind = "种类" + i })).ToArray() };
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
    private static void Render(FrameworkElement root, string? path, int width = 1000, int height = 720)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); Pump();
        var settled = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); settled.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(settled); root.UpdateLayout();
        if (path == null) return;
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file); Images.Add(path);
    }

    private static bool InsideScrollViewports(FrameworkElement element, Rect region)
    {
        int count = 0;
        for (DependencyObject parent = element; parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollContentPresenter viewport) continue;
            count++;
            Point start = element.TranslatePoint(region.TopLeft, viewport), end = element.TranslatePoint(region.BottomRight, viewport);
            if (viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0 || start.X < -1 || start.Y < -1 ||
                end.X > viewport.ActualWidth + 1 || end.Y > viewport.ActualHeight + 1) return false;
        }
        return count >= 2;
    }

    private static void VerifyEntityOverlay(MainViewModel vm, OverlayUiRuntime runtime, TrainerContentView mainView,
        TrainerContentView overlayView, FrameworkElement overlayRoot, TabControl tabs, string output)
    {
        vm.Connect(runtime, CapabilityMatrix.For(SessionMode.Host, false)); Pump();
        vm.SelectedPage = vm.Pages.First(p => p.Category == "战斗"); tabs.SelectedIndex = 1;
        foreach (var view in new[] { mainView, overlayView }) Find<Expander>(Find<ExperienceBar>(view).Single()).Single().IsExpanded = false;
        Render(mainView,null,1400,800);
        Render(overlayRoot,null,620,680);
        var input = vm.EntityOverlayDistanceInput;
        Check(input.Value == 200 && input.LastValidValue == 200 && input.Minimum == 1 && input.Maximum == 1000 && !input.IntegerOnly,
            "Entity annotation starts at 200 m with an inclusive 1–1000 m decimal range");
        var views = new[] { mainView, overlayView };
        var cards = views.Select(view => {
            var title = Find<TextBlock>(view).Single(t => t.Text == "实体标注");
            DependencyObject card = title;
            while (card is not Border) card = VisualTreeHelper.GetParent(card);
            return (Border)card;
        }).ToArray();
        Check(cards.All(card => Find<TextBlock>(card).Any(t => t.Text.Contains("角标") && t.Text.Contains("右 Ctrl"))),
            "Main and floating entity cards describe the lock marker and the right Ctrl hold shortcut");
        var editors = views.Select(view => Find<TextBox>(Find<NumericInputBox>(view).Single(n => ReferenceEquals(n.Input,input))).Single()).ToArray();
        var actions = cards.Select(card => Find<Button>(card).Single(b => Equals(b.Content,"应用显示距离"))).ToArray();
        for (int i=0;i<editors.Length;i++)
        {
            editors[i].BringIntoView(new Rect(0,0,150,editors[i].ActualHeight)); Pump();
            Render(i==0?mainView:overlayRoot,Path.Combine(output,i==0?"entity-overlay-main-default.png":"entity-overlay-panel-default.png"),i==0?1400:620,i==0?800:680);
        }
        Check(cards.All(c => c.Visibility == Visibility.Visible && c.ActualHeight > 0) && views.All(view => Find<NumericInputBox>(view).Count(n => ReferenceEquals(n.Input,input)) == 1)
            && ReferenceEquals(vm.AimAssist.EntityOverlayDistanceInput,input)
            && ReferenceEquals(vm.AllFeatures.Single(f => f.Descriptor.Id == "combat.silent_aim").EntityOverlayDistanceInput,input),
            "Main and floating combat pages each contain one entity card bound to the same display-distance owner");
        Check(editors.All(e => e.Text == "200") && actions.All(b => b.IsEnabled), "Both entity-card editors show the shared default and an available display-only apply action");
        int startCalls = runtime.Calls.Count;
        editors[1].Text = "-"; Pump();
        editors[1].BringIntoView(new Rect(0,0,150,editors[1].ActualHeight)); Pump();
        Render(overlayRoot,Path.Combine(output,"entity-overlay-shared-incomplete.png"),620,680);
        Check(editors.All(e => e.Text == "-") && input.HasError && input.LastValidValue == 200 && actions.All(b => !b.IsEnabled),
            "An unfinished floating-card draft appears unchanged in both views and disables both apply actions without losing 200 m");
        foreach (string value in new[] { "1", "275.5", "1000" })
        {
            editors[0].Text=value; Pump();
            Check(editors.All(e => e.Text == value) && !input.HasError && actions.All(b => b.IsEnabled)
                && input.LastValidValue == double.Parse(value,System.Globalization.CultureInfo.InvariantCulture),
                "Both entity-card editors accept and synchronize the inclusive endpoint or decimal: " + value);
        }
        foreach (string value in new[] { "0.5", "1000.5" })
        {
            editors[1].Text=value; Pump();
            editors[1].BringIntoView(new Rect(0,0,150,editors[1].ActualHeight)); Pump();
            Render(overlayRoot,Path.Combine(output,"entity-overlay-invalid-"+value+".png"),620,680);
            Check(editors.All(e => e.Text == value) && input.HasError && input.LastValidValue == 1000 && actions.All(b => !b.IsEnabled),
                "Both entity cards preserve an out-of-range decimal draft and keep the last valid 1000 m: " + value);
        }
        editors[0].Text="275.5"; Pump();
        for (int i=0;i<views.Length;i++)
        {
            string host=i==0?"main":"panel";
            FrameworkElement root=i==0?mainView:overlayRoot;
            Render(root,Path.Combine(output,"entity-overlay-"+host+"-600.png"),600,380);
            var explanations = Find<TextBlock>(cards[i]).Where(t => t.Text.StartsWith("辅助瞄准或魔法子弹开启时显示") ||
                t.Text.StartsWith("显示距离与两个功能的攻击距离独立")).ToArray();
            Check(explanations.Length == 2 && explanations.All(t => t.TextWrapping == TextWrapping.Wrap && t.TextTrimming == TextTrimming.None && t.ActualHeight > 0),
                "600 by 380 " + host + " entity-card explanations retain their complete wrapping text");
            actions[i].BringIntoView(); Pump();
            Render(root,Path.Combine(output,"entity-overlay-"+host+"-apply-600.png"),600,380);
            Check(actions[i].IsEnabled && actions[i].ActualWidth > 0 && actions[i].ActualHeight > 0 &&
                InsideScrollViewports(actions[i],new Rect(0,0,actions[i].ActualWidth,actions[i].ActualHeight)),
                "600 by 380 " + host + " scrolls the complete entity display apply button through every enclosing viewport");
            for (int text=0;text<explanations.Length;text++)
            foreach (bool last in new[] { false,true })
            {
                var explanation=explanations[text];
                var pointer=last?explanation.ContentEnd:explanation.ContentStart;
                var direction=last?LogicalDirection.Backward:LogicalDirection.Forward;
                Rect character=pointer.GetInsertionPosition(direction)!.GetCharacterRect(direction);
                character.Width=Math.Max(2,character.Width);
                explanation.BringIntoView(character); Pump();
                Render(root,Path.Combine(output,$"entity-overlay-{host}-text-{text}-{(last?"last":"first")}-600.png"),600,380);
                Check(!character.IsEmpty && character.Height > 0 && InsideScrollViewports(explanation,character),
                    "600 by 380 " + host + " can scroll to the " + (last?"last":"first") + " characters of entity explanation " + text);
            }
        }
        var gameplay = runtime.Calls.Skip(startCalls).Where(id => !new[] { "players.catalog", "players.inspect", "catalog.items", "catalog.islands", "features.proofs", "fun.event.catalog", "fun.event.availability", "casino.status" }.Contains(id)).ToArray();
        Check(gameplay.Length == 0,"Editing, validating and scrolling the shared entity card dispatches no runtime write commands");
    }

    private static void VerifyGullChallenge(MainViewModel vm, OverlayUiRuntime runtime, TrainerContentView mainView,
        TrainerContentView overlayView, FrameworkElement overlayRoot, string output)
    {
        int before = runtime.Calls.Count;
        vm.SelectedPage = vm.Pages.Single(p => p.Category == "趣味小功能");
        var views = new[] { mainView, overlayView };
        var roots = new FrameworkElement[] { mainView, overlayRoot };
        Render(mainView,null,1400,800);
        Render(overlayRoot,null,600,380);
        var editors = new List<TextBox>();
        var starts = new List<Button>();
        for (int i = 0; i < views.Length; i++)
        {
            DependencyObject card = Find<TextBlock>(views[i]).Single(t => t.Text == "海鸥飞靶挑战");
            while (card is not Border) card = VisualTreeHelper.GetParent(card);
            var start = Find<Button>(card).Single(b => Equals(b.Content,"开始本轮"));
            var stop = Find<Button>(card).Single(b => Equals(b.Content,"结束本轮"));
            var editor = Find<NumericInputBox>(card).Single();
            starts.Add(start);
            editors.Add(Find<TextBox>(editor).Single());
            string host = i == 0 ? "main" : "panel";
            Check(ReferenceEquals(editor.Input,vm.GullMigration.CountInput) && start.IsEnabled && !stop.IsEnabled
                && Find<TextBlock>(card).Any(t => t.Text.Contains("60 秒")),
                "Ready host shows the shared wave size, timed challenge, enabled start and inactive stop in " + host);
            start.BringIntoView(); Pump();
            Render(roots[i],Path.Combine(output,"gull-challenge-"+host+".png"),i==0?1400:600,i==0?800:380);
            if (i == 1)
                Check(InsideScrollViewports(start,new Rect(0,0,start.ActualWidth,start.ActualHeight)),
                    "The complete start-round button is reachable in the 600 by 380 floating panel");
        }
        editors[1].Text = "-"; Pump();
        Check(editors.All(e => e.Text == "-") && starts.All(b => !b.IsEnabled),
            "An unfinished wave size is shared across both windows and disables both start buttons");
        editors[0].Text = "6"; Pump();
        Check(editors.All(e => e.Text == "6") && starts.All(b => b.IsEnabled),
            "Valid shared wave size makes each start button available again");
        Check(!runtime.Calls.Skip(before).Any(id => id == "fun.challenge.start" || id == "fun.challenge.stop" || id == "fun.event.execute"),
            "Navigation, scrolling and count editing do not start, stop or spawn a gull round");
    }

    private static void VerifyTrayLifecycle(string output)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var store = new OverlayUiPreferenceStore();
        using var license = new HowToFishTrainer.App.Licensing.LicenseService(Path.Combine(output, "tray-license"), "test");
        var window = new MainWindow(license, store);
        var type = typeof(MainWindow);
        var vm = (MainViewModel)type.GetField("_viewModel", instance)!.GetValue(window)!;
        var timer = (DispatcherTimer)type.GetField("_timer", instance)!.GetValue(window)!;
        Check(type.GetField("_trayIcon", instance)!.GetValue(window) == null && !timer.IsEnabled,
            "Preview creates neither a tray icon nor game polling");
        var runtime = new OverlayUiRuntime(); vm.Connect(runtime, CapabilityMatrix.For(SessionMode.Host, false));
        vm.SoundEnabled = false;
        var health = vm.AllFeatures.Single(f => f.Descriptor.Id == "player.health.lock");
        health.Enabled = true; Pump();
        Check(health.Enabled && runtime.Calls.Contains("player.health.lock"), "Tray fixture starts with an enabled simulated health lock");
        using var tray = new System.Windows.Forms.NotifyIcon { Visible = false };
        type.GetField("_trayIcon", instance)!.SetValue(window, tray);
        // Exercise the closing owner without showing a window, registering an icon, or polling a game.
        timer.Interval = TimeSpan.FromDays(1); timer.Start();
        int saves = store.SaveCount, calls = runtime.Calls.Count;
        var cancel = new System.ComponentModel.CancelEventArgs();
        type.GetMethod("OnClosing", instance)!.Invoke(window, new object[] { cancel });
        Check(cancel.Cancel && !window.IsVisible, "Close with a tray owner hides the window instead of exiting");
        Check(timer.IsEnabled && ReferenceEquals(typeof(MainViewModel).GetField("_runtime", instance)!.GetValue(vm), runtime),
            "Hiding preserves the polling timer and the current runtime connection");
        Check(health.Enabled && runtime.Calls.Count == calls && store.SaveCount == saves &&
            !(bool)typeof(MainViewModel).GetField("_preferencesDisposed", instance)!.GetValue(vm)!,
            "Hiding neither disables features nor closes the preference store");
        var exit = type.GetMethod("ExitFromTrayAsync", instance)!;
        var task = (Task)exit.Invoke(window, null)!;
        Check(task.IsCompletedSuccessfully && !timer.IsEnabled && type.GetField("_trayIcon", instance)!.GetValue(window) == null,
            "Local tray exit closes the window and releases the tray owner");
        Check(store.Saved?.Session.Boolean("toggle:player.health.lock") == true &&
            typeof(MainViewModel).GetField("_runtime", instance)!.GetValue(vm) == null,
            "Exit and subsequent OnClosed cleanup retain pre-disconnect enabled switches in saved preferences");
        string saved = store.Saved!.ToJson(); saves = store.SaveCount;
        var repeated = (Task)exit.Invoke(window, null)!;
        Check(repeated.IsCompletedSuccessfully && store.SaveCount == saves && store.Saved.ToJson() == saved,
            "Repeated tray exit leaves the saved session unchanged");
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
        var mainCards = Find<ItemsControl>(secondView).Single(c => ReferenceEquals(c.ItemsSource, vm.DisplayFeatures));
        var mainFirst = (FrameworkElement)mainCards.ItemContainerGenerator.ContainerFromIndex(0);
        var mainNext = (FrameworkElement)mainCards.ItemContainerGenerator.ContainerFromIndex(1);
        Point mainFirstPosition = mainFirst.TranslatePoint(new Point(), secondView), mainNextPosition = mainNext.TranslatePoint(new Point(), secondView);
        Check(Math.Abs(mainFirstPosition.Y - mainNextPosition.Y) < 1 && mainNextPosition.X >= mainFirstPosition.X + mainFirst.ActualWidth - 1, "1400 main view keeps feature cards side by side");
        var mainFeel = vm.FeelFeatures.Take(3).Select(f => Find<NumericInputBox>(secondView).Single(n => ReferenceEquals(n.Input, f.ValueInput))).ToArray();
        var mainFeelPositions = mainFeel.Select(n => n.TranslatePoint(new Point(), secondView)).ToArray();
        Check(mainFeelPositions.Length == 3 && mainFeelPositions.All(p => Math.Abs(p.Y - mainFeelPositions[0].Y) < 1) && mainFeelPositions[2].X > mainFeelPositions[1].X && mainFeelPositions[1].X > mainFeelPositions[0].X, "1400 main view keeps three feel editors on one row");
        foreach (int tab in new[] { 0, 1, 4 }) { tabs.SelectedIndex = tab; Render(content, Path.Combine(output, "default-620-tab-" + tab + ".png"), 620, 680); }
        tabs.SelectedIndex = 1;
        var pageViewport = ((Grid)overlayView.FindName("PageScaleHost")).Children.OfType<ScrollViewer>().Single();
        foreach (var page in vm.Pages)
        {
            vm.SelectedPage = page;
            pageViewport.ScrollToTop(); pageViewport.ScrollToLeftEnd();
            Render(content, Path.Combine(output, "narrow-page-" + page.Category + ".png"), 620, 680);
            bool ordinary = new[] { "玩家", "钓鱼", "金钱背包", "解锁中心" }.Contains(page.Category);
            Check(overlayView.ActualWidth > 400 && overlayView.ActualWidth <= 620 && pageViewport.ViewportWidth > 400 && pageViewport.ViewportWidth <= 620 && (ordinary ? pageViewport.ScrollableWidth < 1 && pageViewport.ScrollableHeight < 1 : pageViewport.ScrollableWidth > 100),
                "620 " + page.Title + (ordinary ? " fits without an extra page scrollbar" : " keeps horizontal scrolling inside the page, not the shared view"));
            if (!ordinary)
            {
                pageViewport.ScrollToBottom(); overlayView.UpdateLayout(); Pump();
                var pageBody = (FrameworkElement)pageViewport.Content;
                var visiblePage = Find<ScrollContentPresenter>(pageViewport).First();
                double bodyBottom = pageBody.TranslatePoint(new Point(0, pageBody.ActualHeight), visiblePage).Y;
                Check(pageViewport.ScrollableHeight > 1 && Math.Abs(pageViewport.VerticalOffset - pageViewport.ScrollableHeight) < 1 && bodyBottom > 0 && bodyBottom <= visiblePage.ActualHeight + 1, "620 " + page.Title + " can scroll to the bottom of its finite page content");
                if (vm.IsCatalogPage)
                {
                    var catalogList = Find<ListBox>(overlayView).Single(l => l.Items.OfType<CatalogOption>().Any());
                    var firstOption = (FrameworkElement)catalogList.ItemContainerGenerator.ContainerFromIndex(0);
                    var visibleList = Find<ScrollContentPresenter>(catalogList).Single();
                    Point optionInList = firstOption.TranslatePoint(new Point(), visibleList), optionInPage = firstOption.TranslatePoint(new Point(), visiblePage);
                    Check(firstOption.ActualHeight > 0 && visibleList.ActualHeight >= firstOption.ActualHeight && visibleList.ActualHeight < overlayView.ActualHeight && optionInList.Y >= -1 && optionInList.Y + firstOption.ActualHeight <= visibleList.ActualHeight + 1 && optionInPage.Y >= -1 && optionInPage.Y + firstOption.ActualHeight <= visiblePage.ActualHeight + 1,
                        "620 " + page.Title + " shows a complete catalog row in a finite list viewport after scrolling");
                    if (vm.IsItemGeneratorPage)
                    {
                        var kindsList = Find<ListBox>((FrameworkElement)overlayView.FindName("GeneratorWorkspace")).Single(l => l.Items.Count > 0 && l.Items.OfType<string>().Count() == l.Items.Count);
                        var kindsScroll = Find<ScrollViewer>(kindsList).Single();
                        kindsScroll.ScrollToEnd();
                        Render(content, Path.Combine(output, "narrow-catalog-last-kind.png"), 620, 680);
                        Console.WriteLine($"Catalog kinds: {kindsList.Items.Count}, offset {kindsScroll.VerticalOffset}/{kindsScroll.ScrollableHeight}, viewport {kindsScroll.ViewportHeight}, extent {kindsScroll.ExtentHeight}, generated {string.Join(",", Enumerable.Range(0, kindsList.Items.Count).Where(i => kindsList.ItemContainerGenerator.ContainerFromIndex(i) != null))}");
                        var lastKind = (FrameworkElement)kindsList.ItemContainerGenerator.ContainerFromIndex(kindsList.Items.Count - 1);
                        var kindsViewport = Find<ScrollContentPresenter>(kindsList).Single();
                        Point lastKindPosition = lastKind.TranslatePoint(new Point(), kindsViewport);
                        Check(kindsList.Items.Count > 10 && kindsScroll.ScrollableHeight > 0 && kindsViewport.ActualHeight < overlayView.ActualHeight && lastKind.ActualHeight > 0 && lastKindPosition.Y >= -1 && lastKindPosition.Y + lastKind.ActualHeight <= kindsViewport.ActualHeight + 1,
                            "620 catalog kind list scrolls to a complete last row with more kinds than the viewport can hold");
                    }
                }
                Render(content, Path.Combine(output, "narrow-bottom-" + page.Category + ".png"), 620, 680);
            }
        }
        var navigation = (ListBox)overlayView.FindName("TopCategories");
        var navigationScroll = Find<ScrollViewer>(navigation).Single();
        var navigationViewport = Find<ScrollContentPresenter>(navigation).Single();
        var lastCategory = (FrameworkElement)navigation.ItemContainerGenerator.ContainerFromItem(vm.Pages.Last());
        Point lastPosition = lastCategory.TranslatePoint(new Point(), navigationViewport);
        Check(navigation.Items.Count == 11 && navigationScroll.HorizontalOffset > 0 && lastPosition.X >= -1 && lastPosition.X + lastCategory.ActualWidth <= navigationViewport.ActualWidth + 1, "620 last category scrolls fully into the navigation viewport");
        double lastOffset = navigationScroll.HorizontalOffset;
        navigationScroll.ScrollToHorizontalOffset(navigationScroll.ScrollableWidth / 2); overlayView.UpdateLayout();
        var capsule = (FrameworkElement)overlayView.FindName("NavCapsule");
        var capsuleLayer = (FrameworkElement)overlayView.FindName("NavCapsuleLayer");
        Point itemPosition = lastCategory.TranslatePoint(new Point(), capsuleLayer), capsulePosition = capsule.TranslatePoint(new Point(), capsuleLayer);
        Check(navigationScroll.HorizontalOffset < lastOffset && Math.Abs(itemPosition.X - capsulePosition.X) < 1 && Math.Abs(itemPosition.Y - capsulePosition.Y) < 1, "Navigation capsule follows a manual middle scroll before any dispatcher wait or animation");
        vm.SelectedPage = vm.Pages.First(p => p.Category == "玩家");
        Render(content, Path.Combine(output, "narrow-player-before-resize.png"), 620, 680);
        var narrowCards = Find<ItemsControl>(overlayView).Single(c => ReferenceEquals(c.ItemsSource, vm.DisplayFeatures));
        var narrowFirst = (FrameworkElement)narrowCards.ItemContainerGenerator.ContainerFromIndex(0);
        var narrowNext = (FrameworkElement)narrowCards.ItemContainerGenerator.ContainerFromIndex(1);
        Point narrowFirstPosition = narrowFirst.TranslatePoint(new Point(), overlayView), narrowNextPosition = narrowNext.TranslatePoint(new Point(), overlayView);
        var narrowFeel = vm.FeelFeatures.Take(2).Select(f => Find<NumericInputBox>(overlayView).Single(n => ReferenceEquals(n.Input, f.ValueInput))).ToArray();
        Check(Math.Abs(narrowFirstPosition.X - narrowNextPosition.X) < 1 && narrowNextPosition.Y >= narrowFirstPosition.Y + narrowFirst.ActualHeight - 1 && narrowFeel[1].TranslatePoint(new Point(), overlayView).Y > narrowFeel[0].TranslatePoint(new Point(), overlayView).Y + narrowFeel[0].ActualHeight, "620 player feature cards and feel editors stack vertically");
        var draftOwner = Find<NumericInputBox>(overlayView).Single(n => ReferenceEquals(n.Input, feature.ValueInput));
        var draftEditor = Find<TextBox>(draftOwner).Single(); draftEditor.Text = "-";
        Render(content, Path.Combine(output, "draft-wide.png"), 1400, 800);
        Render(content, Path.Combine(output, "draft-narrow.png"), 620, 680);
        Check(ReferenceEquals(draftOwner, Find<NumericInputBox>(overlayView).Single(n => ReferenceEquals(n.Input, feature.ValueInput))) && draftEditor.Text == "-" && feature.ValueInput.Text == "-" && feature.ValueInput.LastValidValue == 2.75, "Narrow-wide-narrow resize preserves the same editor and unfinished numeric draft");
        feature.ValueInput.Text = "2.75";
        var populatedPanel = vm.SelectedOverlayPanel;
        vm.OverlayPanelName = "排版检查 · " + new string('长', 30); vm.CreateOverlayPanelCommand.Execute(null); tabs.SelectedIndex = 0;
        Render(content, Path.Combine(output, "empty-panel-620.png"), 620, 680);
        Check(Find<TextBlock>(content).Any(t => t.Text == "把常用操作放在这里" && t.ActualHeight > 0) && Find<Button>(content).Any(b => Equals(b.Content, "添加快捷项") && b.ActualHeight > 0), "Empty shortcut panel shows its explanation and add action at 620");
        var longPoint = new TrainerTeleportPoint(new string('组', 80), new string('长', 80), 1, 2, 3);
        vm.TeleportPoints.Add(longPoint); vm.AddTeleportToOverlayCommand.Execute(longPoint);
        Render(content, Path.Combine(output, "long-shortcut-620.png"), 620, 680);
        var longTitle = Find<TextBlock>(content).Single(t => t.Text == longPoint.Name);
        Check(longTitle.ActualHeight >= longTitle.FontSize * 2 && longTitle.ActualWidth < 620, "Long shortcut title wraps inside the 620 panel");
        tabs.SelectedIndex = 2;
        Render(content, Path.Combine(output, "long-teleport-620.png"), 620, 680);
        var pointName = Find<TextBlock>(content).Single(t => t.Text == longPoint.Name);
        var pointGroup = Find<TextBlock>(content).Single(t => t.Text == longPoint.Group);
        var pointButton = Find<Button>(content).Single(b => ReferenceEquals(b.DataContext, longPoint) && Equals(b.Content, "传送"));
        Check(pointName.ActualHeight >= pointName.FontSize * 2 && pointGroup.ActualHeight >= pointGroup.FontSize * 2 &&
            Equals(pointName.ToolTip, longPoint.Name) && Equals(pointGroup.ToolTip, longPoint.Group) &&
            pointName.TranslatePoint(new Point(pointName.ActualWidth, 0), content).X <= pointButton.TranslatePoint(new Point(), content).X &&
            pointButton.TranslatePoint(new Point(pointButton.ActualWidth, 0), content).X < 620,
            "620 saved-point list wraps full 80-character names and groups beside a reachable action");
        vm.PresetName = new string('预', 40); vm.SavePresetCommand.Execute(null); tabs.SelectedIndex = 3;
        Render(content, Path.Combine(output, "long-preset-620.png"), 620, 680);
        var presetName = Find<TextBlock>(content).Single(t => t.Text == vm.PresetName);
        var presetButton = Find<Button>(content).Single(b => ReferenceEquals(b.DataContext, vm.SelectedPreset) && Equals(b.Content, "恢复参数"));
        Check(presetName.ActualHeight >= presetName.FontSize * 2 && Equals(presetName.ToolTip, vm.PresetName) &&
            presetName.TranslatePoint(new Point(presetName.ActualWidth, 0), content).X <= presetButton.TranslatePoint(new Point(), content).X &&
            presetButton.TranslatePoint(new Point(presetButton.ActualWidth, 0), content).X < 620,
            "620 preset list wraps a full 40-character name beside a reachable restore action");
        vm.SelectedOverlayPanel = populatedPanel; tabs.SelectedIndex = 1;
        vm.SelectedPage = vm.Pages.First(p => p.Category == "鱼类生成");
        Render(content, Path.Combine(output, "fish-overlay.png"));
        Render(secondView, Path.Combine(output, "fish-main.png"), 1400, 800);
        var mainPageViewport = ((Grid)secondView.FindName("PageScaleHost")).Children.OfType<ScrollViewer>().Single();
        Check(mainPageViewport.ScrollableWidth < 1 && mainPageViewport.ScrollableHeight < 1, "1400 catalog page fits without an extra page scrollbar");
        var fishRadio = Find<RadioButton>(overlayView).First(r => Equals(r.Content, "死鱼"));
        fishRadio.IsChecked = true; Pump();
        var fishOwner = (FeatureItemViewModel)fishRadio.DataContext;
        Check(fishOwner.DeadFish && !fishOwner.AliveFish && Find<RadioButton>(secondView).First(r => Equals(r.Content, "死鱼")).IsChecked == true, "Fish selection is synchronized without cross-view radio grouping");
        int callsBeforeSearch = runtime.Calls.Count;
        vm.OverlayPanelName = "搜索快捷检查"; vm.CreateOverlayPanelCommand.Execute(null); tabs.SelectedIndex = 0;
        Render(content, Path.Combine(output, "search-closed-620.png"), 620, 680);
        var searchArea = (FrameworkElement)overlay.FindName("SearchArea");
        var featureSearch = (TextBox)overlay.FindName("FeatureSearch");
        var featureSearchResults = (ComboBox)overlay.FindName("FeatureSearchResults");
        var searchHint = (TextBlock)overlay.FindName("SearchHint");
        var mainExperienceBar = Find<ExperienceBar>(secondView).Single();
        var mainFeatureSearch = (TextBox)mainExperienceBar.FindName("FeatureSearch");
        var mainExperienceExpander = Find<Expander>(mainExperienceBar).Single();
        var activeSummary = (Expander)overlay.FindName("ActiveSummary");
        double closedSearchHeight = tabs.ActualHeight;
        Check(searchArea.Visibility == Visibility.Collapsed && !activeSummary.IsExpanded, "Search and active-feature details start collapsed");
        Find<Button>(content).Single(b => Equals(b.Content, "搜索")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.SelectedOverlayFeatureChoice = vm.OverlayFeatureChoices.Single(choice => choice.Id == "player.health.lock");
        featureSearch.Text = "移动"; Pump();
        var searchHit = vm.SearchResults.Single(hit => hit.Id == "player.move.multiplier");
        featureSearchResults.SelectedItem = searchHit;
        Render(content, Path.Combine(output, "search-results-620.png"), 620, 680);
        Check(searchArea.Visibility == Visibility.Visible && featureSearch.ActualWidth > 100 && Equals(featureSearchResults.SelectedItem, searchHit) &&
            vm.FeatureQuery == "移动" && featureSearch.Text == "移动" && mainFeatureSearch.Text == "移动",
            "Header search opens a usable 620-wide search field with the shared selected result");
        Check(searchHint.Text == "从列表选择结果，可打开分类或加入当前面板。", "Matching search explains how to select a result");
        var addSearch = Find<Button>(searchArea).Single(b => Equals(b.Content, "加入面板"));
        addSearch.Command!.Execute(addSearch.CommandParameter); Pump();
        Check(vm.SelectedOverlayPanel!.Items.Count == 1 && ReferenceEquals(vm.SelectedOverlayPanel.Items.Single().Feature, feature),
            "Search add action reuses the existing feature owner in the selected shortcut panel");
        var openSearch = Find<Button>(searchArea).Single(b => Equals(b.Content, "打开分类"));
        openSearch.Command!.Execute(openSearch.CommandParameter); Pump();
        Check(tabs.SelectedIndex == 1 && vm.SelectedPage.Category == "玩家" && ReferenceEquals(vm.AllFeatures.Single(f => f.Descriptor.Id == searchHit.Id), feature),
            "Search open action selects the full-feature tab without replacing the shared feature");
        mainExperienceExpander.IsExpanded = true;
        Render(secondView, Path.Combine(output, "search-main-results.png"), 1400, 800);
        foreach (var editor in new[] { featureSearch, mainFeatureSearch })
        foreach (string query in new[] { "", "no-matching-feature-78364" })
        {
            editor.Text = "移动"; Pump(); featureSearchResults.SelectedItem = searchHit; Pump();
            editor.Text = query; Pump();
            string source = ReferenceEquals(editor, featureSearch) ? "overlay" : "main";
            Render(content, Path.Combine(output, (source == "main" ? "main-input-" : "") + (query.Length == 0 ? "search-cleared-620.png" : "search-no-results-620.png")), 620, 680);
            Check(vm.FeatureQuery == query && featureSearch.Text == query && mainFeatureSearch.Text == query,
                "Editing " + source + " selected search preserves both inputs: expected=" + JsonSerializer.Serialize(query) +
                ", model=" + JsonSerializer.Serialize(vm.FeatureQuery) + ", overlay=" + JsonSerializer.Serialize(featureSearch.Text) +
                ", main=" + JsonSerializer.Serialize(mainFeatureSearch.Text));
            Check(vm.SelectedSearchHit == null && !openSearch.IsEnabled && !addSearch.IsEnabled && vm.SelectedOverlayPanel!.Items.Count == 1 && vm.SelectedOverlayFeatureChoice?.Id == "player.health.lock",
                (query.Length == 0 ? "Clearing " : "Unmatched ") + source + " search clears selection and disables both actions without adding the settings-page selection");
            Check(searchHint.Text == (query.Length == 0 ? "输入功能名称、说明、分类或 ID 开始搜索。" : "没有匹配的功能，请换个关键词。"),
                source + " search distinguishes an empty query from no matching result");
        }
        mainExperienceExpander.IsExpanded = false;
        tabs.SelectedIndex = 0;
        Find<Button>(searchArea).Single(b => Equals(b.ToolTip, "收起搜索")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(content, Path.Combine(output, "search-hidden-620.png"), 620, 680);
        Check(searchArea.Visibility == Visibility.Collapsed && Math.Abs(tabs.ActualHeight - closedSearchHeight) < 1,
            "Closing search returns its vertical space to the tab content");
        activeSummary.IsExpanded = true;
        Render(content, Path.Combine(output, "active-summary-620.png"), 620, 680);
        Check(vm.ActiveFeaturesText == "持续功能均已关闭" && Find<TextBlock>(activeSummary).Any(t => t.Text == vm.ActiveFeaturesText && t.ActualHeight > 0),
            "Expanded active-feature summary displays the shared all-disabled state");
        Check(runtime.Calls.Count == callsBeforeSearch, "Searching, adding a shortcut, opening its page and expanding the summary dispatch no runtime commands");
        activeSummary.IsExpanded = false; vm.SelectedOverlayPanel = populatedPanel;
        var writes = runtime.Calls.Skip(callsBeforeRender).Where(id => !new[] { "players.catalog", "players.inspect", "catalog.items", "catalog.islands", "features.proofs", "fun.event.catalog", "fun.event.availability", "casino.status" }.Contains(id)).ToArray();
        Check(writes.Length == 0, "Building and navigating overlay sends no gameplay write commands");
        vm.SelectedPage = vm.Pages.First(p => p.Category == "玩家");
        feature.ValueInput.Text = "2.75";
        vm.PresetName = "输入锁定检查"; vm.SavePresetCommand.Execute(null);
        tabs.SelectedIndex = 0;
        Render(content, Path.Combine(output, "preset-editors-ready.png"), 620, 680);
        Render(secondView, Path.Combine(output, "preset-main-ready.png"), 1400, 800);
        var shortcutEditor = Find<TextBox>(Find<NumericInputBox>(content).Single(n => ReferenceEquals(n.Input, feature.ValueInput))).Single();
        var mainEditor = Find<TextBox>(Find<NumericInputBox>(secondView).Single(n => ReferenceEquals(n.Input, feature.ValueInput))).Single();
        Check(shortcutEditor.IsEnabled && mainEditor.IsEnabled, "Shortcut and full-page inputs are editable before preset loading");
        var clear = new TaskCompletionSource<RuntimeCommandResult>(); runtime.PendingClear = clear;
        var loading = vm.LoadOverlayPresetAsync("输入锁定检查"); Pump();
        Check(vm.PresetBusy && !loading.IsCompleted && !shortcutEditor.IsEnabled && !mainEditor.IsEnabled,
            "Pending preset clear disables both views of the shared numeric input");
        Render(content, Path.Combine(output, "preset-busy-620.png"), 620, 680);
        var presetActions = Find<Button>(content).Where(b => b.DataContext is OverlayShortcutViewModel { Kind: "preset" } && Equals(b.Content, "恢复参数")).ToArray();
        Check(presetActions.Length > 0 && presetActions.All(b => !b.IsEnabled) && Find<TextBlock>(content).Any(t => t.DataContext is OverlayShortcutViewModel { Kind: "preset" } && t.Text == "正在处理参数，请稍候"),
            "Pending preset loading disables shortcut restore actions and explains the busy state");
        runtime.PendingClear = null;
        var cleared = JsonSerializer.SerializeToElement(new { applied = true });
        clear.SetResult(new RuntimeCommandResult(true, "fixture clear", cleared, cleared, false)); Pump();
        Render(content, Path.Combine(output, "preset-editors-restored.png"), 620, 680);
        Render(secondView, Path.Combine(output, "preset-main-restored.png"), 1400, 800);
        feature = vm.AllFeatures.Single(f => f.Descriptor.Id == "player.move.multiplier");
        shortcutEditor = Find<TextBox>(Find<NumericInputBox>(content).Single(n => ReferenceEquals(n.Input, feature.ValueInput))).Single();
        mainEditor = Find<TextBox>(Find<NumericInputBox>(secondView).Single(n => ReferenceEquals(n.Input, feature.ValueInput))).Single();
        Check(loading.IsCompletedSuccessfully && !vm.PresetBusy && shortcutEditor.IsEnabled && mainEditor.IsEnabled && feature.ValueInput.Text == "2.75",
            "Completed preset loading restores both editors with the saved value");
        clear = new TaskCompletionSource<RuntimeCommandResult>(); runtime.PendingClear = clear;
        loading = vm.LoadOverlayPresetAsync("输入锁定检查"); Pump();
        vm.ResetWorldContext();
        Render(content, Path.Combine(output, "preset-cancelled-editors.png"), 620, 680);
        Render(secondView, Path.Combine(output, "preset-cancelled-main.png"), 1400, 800);
        feature = vm.AllFeatures.Single(f => f.Descriptor.Id == "player.move.multiplier");
        shortcutEditor = Find<TextBox>(Find<NumericInputBox>(content).Single(n => ReferenceEquals(n.Input, feature.ValueInput))).Single();
        mainEditor = Find<TextBox>(Find<NumericInputBox>(secondView).Single(n => ReferenceEquals(n.Input, feature.ValueInput))).Single();
        Check(!loading.IsCompleted && !vm.PresetBusy && shortcutEditor.IsEnabled && mainEditor.IsEnabled,
            "World change restores both editors before the cancelled preset reply arrives");
        shortcutEditor.Text = "-"; Pump();
        runtime.PendingClear = null;
        clear.SetResult(new RuntimeCommandResult(true, "late fixture clear", cleared, cleared, false)); Pump();
        Check(loading.IsCompletedSuccessfully && shortcutEditor.Text == "-" && mainEditor.Text == "-" && feature.ValueInput.Text == "-" && feature.ValueInput.LastValidValue == 2.75,
            "Late cancelled preset reply preserves the new shared unfinished numeric draft");
        var nativeKeys = typeof(StatusWidget).Assembly.GetType("HowToFishTrainer.App.NativeHotkeys")!;
        var parse = nativeKeys.GetMethod("TryParse", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Parses(string key) { object[] parameters = { key, 0u, 0u }; return (bool)parse.Invoke(null, parameters)!; }
        Check(Parses("Home") && Parses("F8") && Parses("Ctrl+Home") && Parses("OemComma"), "Supported hotkey strings including the named comma key parse");
        Check(!Parses("Ctrl+Shift+F12") && !Parses("Ctrl+Ctrl+Home") && !Parses("Escape") && !Parses("Ctrl++Home") && !Parses("74") && !Parses("Home, End") && !Parses("F1, F2"), "Reserved and invalid hotkeys including comma-combined enum names are rejected");
        vm.Disconnect(); tabs.SelectedIndex = 0;
        content.UpdateLayout(); Pump();
        var teleportAction = Find<Button>(content).Single(b => b.DataContext is OverlayShortcutViewModel { Kind: "teleport" } && Equals(b.Content, "传送"));
        ((FrameworkElement)((FrameworkElement)teleportAction.Parent).Parent).BringIntoView(); Pump();
        Render(content, Path.Combine(output, "offline-shortcuts-620.png"), 620, 680);
        Check(!teleportAction.IsEnabled && Find<TextBlock>(content).Any(t => t.DataContext is OverlayShortcutViewModel { Kind: "teleport" } && t.Text == "当前会话不可传送"),
            "Disconnected shortcut teleport is disabled with a current-session explanation");
        var magicAction = Find<Button>(content).Single(b => b.DataContext is OverlayShortcutViewModel shortcut && shortcut.SourceFeature?.Descriptor.Id == "combat.silent_aim" && Equals(b.Content, "打开设置"));
        Check(((OverlayShortcutViewModel)magicAction.DataContext).SourceFeature?.IsAvailable == false && magicAction.IsEnabled && Find<TextBlock>(content).Any(t => t.DataContext == magicAction.DataContext && t.Text == "暂不可用 · 可查看设置"),
            "Unavailable complex feature still allows opening its settings and explains that distinction");
        vm.TeleportPoints.Remove(point);
        content.UpdateLayout(); Pump();
        teleportAction = Find<Button>(content).Single(b => b.DataContext is OverlayShortcutViewModel { Kind: "teleport", IsAvailable: false } && Equals(b.Content, "传送"));
        ((FrameworkElement)((FrameworkElement)teleportAction.Parent).Parent).BringIntoView(); Pump();
        Render(content, Path.Combine(output, "invalid-shortcut-620.png"), 620, 680);
        Check(!teleportAction.IsEnabled && Find<TextBlock>(content).Any(t => t.DataContext == teleportAction.DataContext && t.Text == "已失效，请重新添加"),
            "Missing saved-point reference disables its main action and shows how to repair it");
        Find<Button>(content).Single(b => Equals(b.Content, "搜索")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        activeSummary.IsExpanded = true;
        foreach (int shortWidth in new[] { 620, 600 })
        {
            Render(content, Path.Combine(output, "short-search-summary-" + shortWidth + ".png"), shortWidth, 380);
            var shortcutItems = Find<ItemsControl>(content).Single(c => ReferenceEquals(c.ItemsSource, vm.SelectedOverlayPanel!.Items));
            DependencyObject scrollParent = shortcutItems;
            while (scrollParent is not ScrollViewer) scrollParent = VisualTreeHelper.GetParent(scrollParent);
            var shortcutScroll = (ScrollViewer)scrollParent;
            Check(shortcutScroll.ViewportHeight > 0 && shortcutScroll.ActualHeight > 0,
                shortWidth + " by 380 retains a positive shortcut viewport with search and summary expanded: " + shortcutScroll.ViewportHeight);
            var shortAction = Find<Button>(content).Single(b => b.DataContext is OverlayShortcutViewModel item && item.SourceFeature?.Descriptor.Id == "combat.silent_aim" && Equals(b.Content, "打开设置"));
            shortAction.BringIntoView(); Pump();
            Render(content, Path.Combine(output, "short-action-reachable-" + shortWidth + ".png"), shortWidth, 380);
            var shortViewport = Find<ScrollContentPresenter>(shortcutScroll).First();
            Point shortPosition = shortAction.TranslatePoint(new Point(), shortViewport);
            Check(shortAction.ActualHeight > 0 && shortPosition.Y >= -1 && shortPosition.Y + shortAction.ActualHeight <= shortViewport.ActualHeight + 1,
                shortWidth + " by 380 can scroll to a complete shortcut action with search and summary expanded");
        }
        vm.OverlayStatus = "设置未保存到磁盘\n无法访问路径 D:\\用户设置\\" + new string('长', 120) + "\\experience.json\n请检查权限后重试。\n更改尚未写入磁盘。";
        Render(content, Path.Combine(output, "short-long-status-600.png"), 600, 380);
        var finalAction = Find<Button>(content).Single(b => b.DataContext is OverlayShortcutViewModel item && item.SourceFeature?.Descriptor.Id == "combat.silent_aim" && Equals(b.Content, "打开设置"));
        finalAction.BringIntoView(); Pump();
        Render(content, Path.Combine(output, "short-long-status-action-600.png"), 600, 380);
        var finalViewport = Find<ScrollContentPresenter>(content).Single(v => Find<ItemsControl>(v).Any(c => ReferenceEquals(c.ItemsSource, vm.SelectedOverlayPanel!.Items)));
        Point finalPosition = finalAction.TranslatePoint(new Point(), finalViewport);
        var feedback = Find<TextBlock>(content).Single(t => t.Text == vm.OverlayStatus);
        Check(finalAction.ActualHeight > 0 && finalPosition.Y >= -1 && finalPosition.Y + finalAction.ActualHeight <= finalViewport.ActualHeight + 1 && feedback.ActualHeight <= 16 && Equals(feedback.ToolTip, vm.OverlayStatus),
            "600 by 380 keeps a complete shortcut action reachable with a multiline error and long path while retaining full feedback in its tooltip: " + finalViewport.ActualHeight);
        foreach (int compactTab in new[] { 2, 3 })
        {
            tabs.SelectedIndex = compactTab;
            string statusText = "操作失败\n无法访问配置：" + new string('长', 100) + "\n请检查后重试。\n详细错误保留。";
            typeof(MainViewModel).GetProperty(compactTab == 2 ? "PositionStatus" : "ExperienceStatus")!.SetValue(vm, statusText);
            Render(content, Path.Combine(output, "short-page-" + compactTab + "-600.png"), 600, 380);
            var pageScroll = (ScrollViewer)((TabItem)tabs.SelectedItem).Content;
            var outerViewport = Find<ScrollContentPresenter>(pageScroll).First();
            var list = Find<ListBox>(pageScroll).Single();
            var listViewport = Find<ScrollContentPresenter>(list).Single();
            var action = Find<Button>(list).First(b => Equals(b.Content, compactTab == 2 ? "传送" : "恢复参数"));
            action.BringIntoView(); Pump();
            Render(content, Path.Combine(output, "short-page-action-" + compactTab + "-600.png"), 600, 380);
            Point actionInList = action.TranslatePoint(new Point(), listViewport), actionInPage = action.TranslatePoint(new Point(), outerViewport);
            Check(listViewport.ActualHeight >= 40 && listViewport.ActualHeight < 240 && action.ActualHeight > 0 &&
                actionInList.Y >= -1 && actionInList.Y + action.ActualHeight <= listViewport.ActualHeight + 1 &&
                actionInPage.Y >= -1 && actionInPage.Y + action.ActualHeight <= outerViewport.ActualHeight + 1,
                "600 by 380 tab " + compactTab + " scrolls a complete list action into both finite viewports with search and multiline status: " + listViewport.ActualHeight);
            var footerAction = Find<Button>(pageScroll).Single(b => Equals(b.Content, compactTab == 2 ? "将选中传送点加入当前面板" : "删除预设"));
            footerAction.BringIntoView(); Pump();
            Render(content, Path.Combine(output, "short-page-footer-" + compactTab + "-600.png"), 600, 380);
            Point footerPosition = footerAction.TranslatePoint(new Point(), outerViewport);
            var status = Find<TextBlock>(pageScroll).Single(t => t.Text == statusText);
            Check(footerAction.ActualHeight > 0 && footerPosition.Y >= -1 && footerPosition.Y + footerAction.ActualHeight <= outerViewport.ActualHeight + 1 && status.ActualHeight <= 32 && Equals(status.ToolTip, statusText),
                "600 by 380 tab " + compactTab + " keeps its footer action reachable and full multiline feedback in the tooltip");
            Render(content, Path.Combine(output, "normal-page-" + compactTab + "-620.png"), 620, 680);
            Check(pageScroll.ScrollableHeight < 1 && listViewport.ActualHeight > 100,
                "620 by 680 tab " + compactTab + " retains the full list without an extra outer scrollbar");
        }
        vm.OverlayPanelName = new string('长', 40); vm.RenameOverlayPanelCommand.Execute(null);
        foreach (int panelTab in new[] { 0, 4 })
        {
            tabs.SelectedIndex = panelTab;
            Render(content, Path.Combine(output, "long-panel-name-" + panelTab + "-600.png"), 600, 380);
            var panelSelector = Find<ComboBox>(content).Single(c => ReferenceEquals(c.ItemsSource, vm.OverlayPanels));
            panelSelector.BringIntoView(); Pump();
            Render(content, Path.Combine(output, "long-panel-selector-" + panelTab + "-600.png"), 600, 380);
            Check(Equals(panelSelector.ToolTip, vm.SelectedOverlayPanel!.Name) && panelSelector.ActualWidth > 100 && panelSelector.Focusable && panelSelector.IsTabStop,
                "600 by 380 tab " + panelTab + " preserves the full 40-character selected panel name and a keyboard-focusable selector");
        }
        var settingsScroll = (ScrollViewer)((TabItem)tabs.SelectedItem).Content;
        var settingsViewport = Find<ScrollContentPresenter>(settingsScroll).First();
        var hotkeyEditor = Find<TextBox>(settingsScroll).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "展开或收起快捷键");
        hotkeyEditor.BringIntoView(); Pump();
        Render(content, Path.Combine(output, "short-settings-hotkey-600.png"), 600, 380);
        Point hotkeyPosition = hotkeyEditor.TranslatePoint(new Point(), settingsViewport);
        Check(hotkeyEditor.IsEnabled && hotkeyEditor.Focusable && hotkeyEditor.IsTabStop && hotkeyEditor.ActualWidth >= 150 && hotkeyPosition.Y >= -1 && hotkeyPosition.Y + hotkeyEditor.ActualHeight <= settingsViewport.ActualHeight + 1,
            "600 by 380 settings scrolls the keyboard-focusable hotkey editor completely into view");
        Check(featureSearch.Focusable && featureSearch.IsTabStop && featureSearchResults.Focusable && featureSearchResults.IsTabStop,
            "Separate search input and result selector remain in keyboard tab navigation");
        Check(!Parses("Ctrl+F"), "Overlay toggle cannot reserve its local search shortcut Ctrl+F");
        tabs.SelectedIndex = 1;
        vm.SelectedPage = vm.Pages.First(p => p.Category == "玩家");
        feature = vm.AllFeatures.Single(f => f.Descriptor.Id == "player.move.multiplier");
        typeof(MainViewModel).GetProperty("ExperienceStatus")!.SetValue(vm, "就绪");
        Find<Button>(searchArea).Single(b => Equals(b.ToolTip, "收起搜索")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var allFeaturesScroll = (ScrollViewer)((TabItem)tabs.SelectedItem).Content;
        var allFeaturesViewport = Find<ScrollContentPresenter>(allFeaturesScroll).First();
        var overlayExperience = Find<ExperienceBar>(overlayView).Single();
        Find<Expander>(overlayExperience).Single().IsExpanded = true;
        Render(content, Path.Combine(output, "expanded-experience-600-680.png"), 600, 680);
        Check(allFeaturesScroll.ScrollableHeight < 1, "600 by 680 full-feature page adds no outer scrollbar with the experience controls expanded");
        foreach (bool headerSearchOpen in new[] { false, true })
        {
            if (headerSearchOpen) Find<Button>(content).Single(b => Equals(b.Content, "搜索")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(content, Path.Combine(output, "short-all-features-" + headerSearchOpen + ".png"), 600, 380);
            var overlayFeatureSearch = (TextBox)overlayExperience.FindName("FeatureSearch");
            overlayFeatureSearch.BringIntoView(); Pump();
            Render(content, Path.Combine(output, "short-experience-search-" + headerSearchOpen + ".png"), 600, 380);
            Point searchPosition = overlayFeatureSearch.TranslatePoint(new Point(), allFeaturesViewport);
            Check(overlayFeatureSearch.ActualWidth > 100 && overlayFeatureSearch.ActualHeight > 0 && searchPosition.Y >= -1 && searchPosition.Y + overlayFeatureSearch.ActualHeight <= allFeaturesViewport.ActualHeight + 1,
                "600 by 380 full-feature page scrolls its experience search input into view with header search " + headerSearchOpen);
            var feelEditor = Find<NumericInputBox>(overlayView).Single(n => ReferenceEquals(n.Input, feature.ValueInput));
            var feelTextbox = Find<TextBox>(feelEditor).Single();
            feelTextbox.BringIntoView(); Pump();
            Render(content, Path.Combine(output, "short-feature-editor-" + headerSearchOpen + ".png"), 600, 380);
            Point editorInPage = feelTextbox.TranslatePoint(new Point(), allFeaturesViewport);
            var bodyViewport = Find<ScrollContentPresenter>(pageViewport).First();
            Point editorInBody = feelTextbox.TranslatePoint(new Point(), bodyViewport);
            Check(feelTextbox.ActualHeight > 0 && editorInBody.Y >= -1 && editorInBody.Y + feelTextbox.ActualHeight <= bodyViewport.ActualHeight + 1 &&
                editorInPage.Y >= -1 && editorInPage.Y + feelTextbox.ActualHeight <= allFeaturesViewport.ActualHeight + 1,
                "600 by 380 full-feature page scrolls a numeric editor into both finite viewports with header search " + headerSearchOpen);
        }
        string longExperienceStatus = "预设恢复失败\n无法访问配置：" + new string('长', 200) + "\n请检查后重试。\n详细错误保留。";
        typeof(MainViewModel).GetProperty("ExperienceStatus")!.SetValue(vm, longExperienceStatus);
        Render(content, Path.Combine(output, "short-experience-long-status.png"), 600, 380);
        var longStatusEditor = Find<TextBox>(Find<NumericInputBox>(overlayView).Single(n => ReferenceEquals(n.Input, feature.ValueInput))).Single();
        longStatusEditor.BringIntoView(); Pump();
        Render(content, Path.Combine(output, "short-experience-long-status-editor.png"), 600, 380);
        var longStatusBodyViewport = Find<ScrollContentPresenter>(pageViewport).First();
        Point longStatusEditorInBody = longStatusEditor.TranslatePoint(new Point(), longStatusBodyViewport), longStatusEditorInPage = longStatusEditor.TranslatePoint(new Point(), allFeaturesViewport);
        Console.WriteLine("Expanded experience with multiline status: body viewport=" + longStatusBodyViewport.ActualHeight + ", editor height=" + longStatusEditor.ActualHeight);
        Check(longStatusEditor.ActualHeight > 0 && longStatusEditorInBody.Y >= -1 && longStatusEditorInBody.Y + longStatusEditor.ActualHeight <= longStatusBodyViewport.ActualHeight + 1 &&
            longStatusEditorInPage.Y >= -1 && longStatusEditorInPage.Y + longStatusEditor.ActualHeight <= allFeaturesViewport.ActualHeight + 1,
            "600 by 380 full-feature page preserves a completely reachable numeric editor after a multiline experience error");
        var longStatusText = Find<TextBlock>(overlayExperience).Single(t => t.Text == longExperienceStatus);
        Check(longStatusText.ActualHeight <= 32 && Equals(longStatusText.ToolTip, longExperienceStatus),
            "Experience feedback keeps a compact height and retains the complete multiline error in its tooltip");
        VerifyEntityOverlay(vm,runtime,secondView,overlayView,content,tabs,output);
        VerifyGullChallenge(vm,runtime,secondView,overlayView,content,output);
        VerifyTrayLifecycle(output);
        listener.Flush(); string bindingErrors = errors.ToString(); File.WriteAllText(Path.Combine(output, "binding-errors.txt"), bindingErrors);
        Check(string.IsNullOrWhiteSpace(bindingErrors), "No WPF binding errors during all overlay and feature-page rendering");
        vm.CloseExperience(); vm.ClosePreferences(); main.Close();
        File.WriteAllText(Path.Combine(output, "ui-verification.json"), JsonSerializer.Serialize(new { verificationVersion = "20261113-system-tray-ui-v12", appAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(StatusWidget).Assembly.Location))), runnerAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(OverlayUiVerification).Assembly.Location))), checks = Checks, images = Images, realGameOpened = false, desktopInputSent = false, formalPreferencesUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS " + Checks.Count + " UI checks; " + Images.Count + " rendered images");
        return 0;
    }
}
