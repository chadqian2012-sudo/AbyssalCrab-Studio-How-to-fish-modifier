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
        var longPoint = new TrainerTeleportPoint("排版检查", "海岸营地 · " + new string('长', 40), 1, 2, 3);
        vm.TeleportPoints.Add(longPoint); vm.AddTeleportToOverlayCommand.Execute(longPoint);
        Render(content, Path.Combine(output, "long-shortcut-620.png"), 620, 680);
        var longTitle = Find<TextBlock>(content).Single(t => t.Text == longPoint.Name);
        Check(longTitle.ActualHeight >= longTitle.FontSize * 2 && longTitle.ActualWidth < 620, "Long shortcut title wraps inside the 620 panel");
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
        Check(Parses("Home") && Parses("F8") && Parses("Ctrl+Home"), "Supported hotkey strings parse");
        Check(!Parses("Ctrl+Shift+F12") && !Parses("Ctrl+Ctrl+Home") && !Parses("Escape") && !Parses("Ctrl++Home") && !Parses("74"), "Reserved and invalid hotkeys are rejected");
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
        listener.Flush(); string bindingErrors = errors.ToString(); File.WriteAllText(Path.Combine(output, "binding-errors.txt"), bindingErrors);
        Check(string.IsNullOrWhiteSpace(bindingErrors), "No WPF binding errors during all overlay and feature-page rendering");
        vm.CloseExperience(); vm.ClosePreferences(); main.Close();
        File.WriteAllText(Path.Combine(output, "ui-verification.json"), JsonSerializer.Serialize(new { verificationVersion = "20261106-overlay-ui-v6", appAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(StatusWidget).Assembly.Location))), runnerAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(OverlayUiVerification).Assembly.Location))), checks = Checks, images = Images, realGameOpened = false, desktopInputSent = false, formalPreferencesUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS " + Checks.Count + " UI checks; " + Images.Count + " rendered images");
        return 0;
    }
}
