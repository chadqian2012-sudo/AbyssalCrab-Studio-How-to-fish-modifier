using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HowToFishTrainer.App;

public sealed partial class StatusWidget : Window, IDisposable
{
    private const int OverlayHotkeyId = 4740167;
    private const double LauncherSize = 52;
    private readonly MainViewModel _vm;
    private readonly Window _main;
    private readonly Func<int?> _processId;
    private readonly DispatcherTimer _timer;
    private readonly bool _preview;
    private nint _handle, _gameWindow;
    private int? _gameProcess;
    private int _expandAnimationVersion;
    private HwndSource? _source;
    private string? _registeredHotkey, _failedHotkey;
    private bool _disposed, _expanded, _pointerDown, _dragged;
    private Point _dragStart;
    private double _dragLeft, _dragTop;
    private UIElement? _dragElement;

    public StatusWidget(MainViewModel vm, Window main, Func<int?> processId, Action<int>? openJournal = null)
    {
        _vm = vm; _main = main; _processId = processId;
        _preview = Environment.GetCommandLineArgs().Contains("--ui-preview", StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        DataContext = vm;
        TrainerContent.OpenJournalRequested = tab => { SetExpanded(false, false); openJournal?.Invoke(tab); };
        Width = Height = LauncherSize;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += Tick;
        _vm.PropertyChanged += ModelChanged;
        _vm.OverlayNavigationRequested += Navigate;
        KeyDown += HandleKey;
        LostMouseCapture += HandleLostCapture;
        new WindowInteropHelper(this).EnsureHandle();
        SyncEnabled();
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WindowMessage);
        UpdateActivationStyle();
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 786 && wParam == OverlayHotkeyId)
        {
            if (TryGameClient(out _, out _, out _) && IsRelevantForeground()) SetExpanded(!_expanded, true);
            handled = true;
        }
        else if (message == 33 && !_expanded) { handled = true; return 3; }
        return 0;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.WidgetVisible)) SyncEnabled();
        else if (e.PropertyName == nameof(MainViewModel.OverlayHotkey)) { _failedHotkey = null; SyncHotkey(IsVisible && !_preview); }
    }
    private void SyncEnabled()
    {
        if (_disposed) return;
        if (!_vm.WidgetVisible) { _timer.Stop(); SyncHotkey(false); SetExpanded(false, IsActive); Hide(); }
        else if (!_preview) { _timer.Start(); Tick(null, EventArgs.Empty); }
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        bool visible = _vm.WidgetVisible && TryGameClient(out _, out _, out _) && IsRelevantForeground();
        if (!visible)
        {
            SyncHotkey(false); SetExpanded(false, false); _dragElement?.ReleaseMouseCapture();
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible) Show();
        PlaceInsideGame(); SyncHotkey(true);
    }
    private bool TryGameClient(out NativeRect rect, out NativePoint origin, out double scale)
    {
        rect = default; origin = default; scale = 1;
        int? pid = _processId();
        if (!pid.HasValue) { _gameWindow = 0; _gameProcess = null; return false; }
        if (_gameProcess != pid) { _gameProcess = pid; _gameWindow = 0; }
        var front = NativeOverlay.GetForegroundWindow();
        NativeOverlay.GetWindowThreadProcessId(front, out uint frontPid);
        if (frontPid == pid.Value) _gameWindow = front;
        if (_gameWindow != 0)
        {
            NativeOverlay.GetWindowThreadProcessId(_gameWindow, out uint cachedPid);
            if (!NativeOverlay.IsWindow(_gameWindow) || cachedPid != pid.Value) _gameWindow = 0;
        }
        if (_gameWindow == 0)
        {
            try { using var game = Process.GetProcessById(pid.Value); _gameWindow = game.MainWindowHandle; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
        }
        if (_gameWindow == 0 || NativeOverlay.IsIconic(_gameWindow) || !NativeOverlay.GetClientRect(_gameWindow, out rect) || rect.Right <= 0 || rect.Bottom <= 0) return false;
        if (!NativeOverlay.ClientToScreen(_gameWindow, ref origin)) return false;
        uint dpi = NativeOverlay.GetDpiForWindow(_gameWindow);
        scale = dpi > 0 ? dpi / 96d : 1;
        return true;
    }
    private bool IsRelevantForeground()
    {
        var front = NativeOverlay.GetForegroundWindow();
        var mainHandle = new WindowInteropHelper(_main).Handle;
        for (int depth = 0; front != 0 && depth < 12; depth++)
        {
            if (front == _gameWindow || front == _handle || front == mainHandle) return true;
            front = NativeOverlay.GetWindow(front, 4);
        }
        return false;
    }
    private void PlaceInsideGame()
    {
        if (!TryGameClient(out var rect, out var origin, out double scale)) return;
        double availableWidth = rect.Right / scale, availableHeight = rect.Bottom / scale;
        double width = _expanded ? Math.Clamp(_vm.WidgetWidth, Math.Min(600, availableWidth), availableWidth) : Math.Min(LauncherSize, availableWidth);
        double height = _expanded ? Math.Clamp(_vm.WidgetHeight, Math.Min(380, availableHeight), availableHeight) : Math.Min(LauncherSize, availableHeight);
        Width = width; Height = height;
        double left = Math.Clamp(_vm.WidgetLeft, 0, Math.Max(0, availableWidth - width));
        double top = Math.Clamp(_vm.WidgetTop, 0, Math.Max(0, availableHeight - height));
        NativeOverlay.SetWindowPos(_handle, 0, origin.X + (int)Math.Round(left * scale), origin.Y + (int)Math.Round(top * scale), (int)Math.Round(width * scale), (int)Math.Round(height * scale), 0x0004 | 0x0010);
    }
    private void UpdateActivationStyle()
    {
        if (_handle == 0) return;
        long style = NativeOverlay.GetWindowLongPtr(_handle, -20).ToInt64();
        style = _expanded ? style & ~0x08000000L : style | 0x08000000L;
        NativeOverlay.SetWindowLongPtr(_handle, -20, new IntPtr(style));
    }
    private void SetExpanded(bool expanded, bool returnFocus)
    {
        if (_expanded == expanded) return;
        if (expanded && !TryGameClient(out _, out _, out _)) { _vm.OverlayStatus = "进入游戏窗口后可展开游戏面板"; return; }
        bool wasActive = IsActive;
        _expanded = expanded;
        int animationVersion = ++_expandAnimationVersion;
        ExpandedSurface.BeginAnimation(OpacityProperty, null);
        ExpandedSurface.Opacity = 1;
        Launcher.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedSurface.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateActivationStyle(); PlaceInsideGame();
        if (expanded)
        {
            if (!IsVisible) Show(); Activate();
            Duration duration = TryFindResource("MotionStatus") is Duration value ? value : new Duration(TimeSpan.FromMilliseconds(120));
            var fade = new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop, EasingFunction = TryFindResource("EaseEnter") as IEasingFunction };
            fade.Completed += (_, _) => { if (animationVersion == _expandAnimationVersion && !_disposed) ExpandedSurface.BeginAnimation(OpacityProperty, null); };
            ExpandedSurface.BeginAnimation(OpacityProperty, fade);
        }
        else if (returnFocus && wasActive && TryGameClient(out _, out _, out _)) NativeOverlay.SetForegroundWindow(_gameWindow);
    }
    private void SyncHotkey(bool enabled)
    {
        string requested = _vm.OverlayHotkey.Trim();
        if (_registeredHotkey != null && (!enabled || !string.Equals(_registeredHotkey, requested, StringComparison.OrdinalIgnoreCase)))
        { NativeHotkeys.UnregisterHotKey(_handle, OverlayHotkeyId); _registeredHotkey = null; }
        if (!enabled) { _failedHotkey = null; return; }
        if (_registeredHotkey != null || _failedHotkey == requested) return;
        if (!NativeHotkeys.TryParse(requested, out uint modifiers, out uint key))
        { _failedHotkey = requested; _vm.OverlayStatus = "快捷键无效，请填写 Home、F8 或 Ctrl+Home；紧急停用组合键不可占用"; return; }
        if (!NativeHotkeys.RegisterHotKey(_handle, OverlayHotkeyId, modifiers | 0x4000u, key))
        { _failedHotkey = requested; _vm.OverlayStatus = "快捷键已被占用，请在设置中更换；仍可点击悬浮按钮"; return; }
        _registeredHotkey = requested;
        _vm.OverlayStatus = requested + " 展开 / 收起 · Ctrl + Shift + F12 紧急停用";
    }
    private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !TryGameClient(out _, out var origin, out double scale)) return;
        _dragStart = PointToScreen(e.GetPosition(this));
        var windowOrigin = PointToScreen(new Point());
        _dragLeft = (windowOrigin.X - origin.X) / scale; _dragTop = (windowOrigin.Y - origin.Y) / scale;
        _pointerDown = true; _dragged = false; _dragElement = (UIElement)sender;
        _dragElement.CaptureMouse(); e.Handled = true;
    }
    private void Drag_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed || !TryGameClient(out var rect, out _, out double scale)) return;
        Point point = PointToScreen(e.GetPosition(this));
        double dx = (point.X - _dragStart.X) / scale, dy = (point.Y - _dragStart.Y) / scale;
        if (!_dragged && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragged = true;
        _vm.WidgetLeft = Math.Clamp(_dragLeft + dx, 0, Math.Max(0, rect.Right / scale - Width));
        _vm.WidgetTop = Math.Clamp(_dragTop + dy, 0, Math.Max(0, rect.Bottom / scale - Height));
        PlaceInsideGame(); e.Handled = true;
    }
    private void Drag_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown || e.ChangedButton != MouseButton.Left) return;
        _pointerDown = false; _dragElement?.ReleaseMouseCapture(); _dragElement = null;
        if (!_dragged && ReferenceEquals(sender, Launcher)) SetExpanded(true, false);
        e.Handled = true;
    }
    private void HandleLostCapture(object sender, MouseEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, _dragElement)) { _pointerDown = false; _dragElement = null; } }
    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_expanded || !TryGameClient(out var rect, out _, out double scale)) return;
        _vm.WidgetWidth = Math.Clamp(Width + e.HorizontalChange, Math.Min(600, rect.Right / scale), rect.Right / scale);
        _vm.WidgetHeight = Math.Clamp(Height + e.VerticalChange, Math.Min(380, rect.Bottom / scale), rect.Bottom / scale);
        PlaceInsideGame();
    }
    private void Navigate(string category) { OverlayTabs.SelectedIndex = category == "@presets" ? 3 : 1; SetExpanded(true, false); }
    private void HandleKey(object sender, KeyEventArgs e) { if (!e.Handled && e.Key == Key.Escape && _expanded) { SetExpanded(false, true); e.Handled = true; } }
    private void Collapse_Click(object sender, RoutedEventArgs e) => SetExpanded(false, true);
    private void Settings_Click(object sender, RoutedEventArgs e) => OverlayTabs.SelectedIndex = 4;
    private void TeleportPage_Click(object sender, RoutedEventArgs e) { _vm.SelectedPage = _vm.Pages.First(p => p.Category == "世界传送"); OverlayTabs.SelectedIndex = 1; }
    protected override void OnClosing(CancelEventArgs e) { if (!_disposed) { e.Cancel = true; SetExpanded(false, true); } base.OnClosing(e); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _expandAnimationVersion++; ExpandedSurface.BeginAnimation(OpacityProperty, null); _timer.Stop(); _timer.Tick -= Tick;
        SyncHotkey(false); _source?.RemoveHook(WindowMessage);
        _vm.PropertyChanged -= ModelChanged; _vm.OverlayNavigationRequested -= Navigate;
        KeyDown -= HandleKey; LostMouseCapture -= HandleLostCapture; _dragElement?.ReleaseMouseCapture(); Close();
    }
}
