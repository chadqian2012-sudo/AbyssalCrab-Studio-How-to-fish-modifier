using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using HowToFishTrainer.App.Licensing;
using HowToFishTrainer.Core;
using HowToFishTrainer.Memory;

namespace HowToFishTrainer.App;

public partial class MainWindow : Window, IComponentConnector
{
	private readonly ILicenseClient _license;

	private readonly bool _ownsLicense;

	private readonly SponsorAccessGate _sponsorGate;

	private readonly MainViewModel _viewModel;

	private readonly BridgeAttachPolicy _attachPolicy = new BridgeAttachPolicy();

	private readonly CompatibilityUpdateNoticeTracker _compatibilityNotice = new CompatibilityUpdateNoticeTracker();

	private readonly DispatcherTimer _timer;

	private readonly CrosshairOverlay _crosshairOverlay;

	private RuntimeBridgeClient? _bridge;

	private LicensedRuntimeClient? _licensedRuntime;

	private bool _attaching;

	private bool _refreshingMode;

	private int? _attachedProcessId;

	private SessionMode? _sessionMode;

	private ulong? _sceneHandle;

	private Guid? _consentSession;

	private string? _consentLobbyId;

	private int? _consentOwnerId;

	private bool _consentGranted;

	private string? _compatibilityCacheKey;

	private CompatibilityReport? _compatibilityCache;

	private const int EmergencyHotkeyId = 4740166;

	public MainWindow()
		: this(new LicenseService(ProductIdentity.AppDataDirectory, "1.1.37"), ownsLicense: true, CreatePreferencesStore())
	{
	}

	public MainWindow(ILicenseClient license)
		: this(license, ownsLicense: false, null)
	{
	}

    public MainWindow(ILicenseClient license, IUserPreferencesStore? preferencesStore)
        : this(license, ownsLicense: false, preferencesStore) { }

    internal static IUserPreferencesStore? CreatePreferencesStore() =>
        Environment.GetCommandLineArgs().Contains("--ui-preview", StringComparer.OrdinalIgnoreCase) ? null :
        new FileUserPreferencesStore(Path.Combine(ProductIdentity.AppDataDirectory, "user-preferences.json"));

	private MainWindow(ILicenseClient license, bool ownsLicense, IUserPreferencesStore? preferencesStore)
	{
		_license = license;
		_ownsLicense = ownsLicense;
		_sponsorGate = new SponsorAccessGate(() => _license.Current);
		_viewModel = new MainViewModel(null, _sponsorGate, (string title, string message) =>
		{
			MessageBox.Show(_statusWidget?.IsActive == true ? _statusWidget : this, message, title, MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}, preferencesStore);
		_sponsorGate.ActivationRequested += SponsorGate_ActivationRequested;
		_license.StatusChanged += License_StatusChanged;
		InitializeComponent();
		DataContext = _viewModel;
        TrainerContent.OpenJournalRequested = OpenJournal;
		_crosshairOverlay = new CrosshairOverlay(_viewModel.Crosshair, () => _attachedProcessId, Dispatcher);
		_viewModel.UpdateLicenseStatus(_license.Current);
        // The callback constructor starts immediately, including in preview windows.
        // Configure a stopped timer so preview/tests cannot claim a running game's bridge.
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += PollGame;
		if (!Environment.GetCommandLineArgs().Contains("--ui-preview", StringComparer.OrdinalIgnoreCase))
		{
			_timer.Start();
		}
        InitializeExperienceUi();
	}

    private bool _appearanceClosing;


	// ---- 材质强度菜单（纯外观设置） ------------------------------------------------------
	// 三档只影响玻璃材质 token；不触碰任何功能、参数、协议或连接路径。
	// 勾选状态按当前实际档位同步，避免出现"菜单显示 A、界面是 B"。

	private void MaterialSolid_Click(object sender, RoutedEventArgs e) => SelectMaterial(MaterialStrength.Solid);

	private void MaterialTinted_Click(object sender, RoutedEventArgs e) => SelectMaterial(MaterialStrength.Tinted);

	private void MaterialClear_Click(object sender, RoutedEventArgs e) => SelectMaterial(MaterialStrength.Clear);

	private void SelectMaterial(MaterialStrength strength)
	{
		App.SwitchMaterialTier(strength);
		SyncMaterialMenu();
	}

	private void SyncMaterialMenu()
	{
        MaterialStrength actual = App.EffectiveMaterialStrength;
		MaterialSolidItem.IsChecked = actual == MaterialStrength.Solid;
		MaterialTintedItem.IsChecked = actual == MaterialStrength.Tinted;
		MaterialClearItem.IsChecked = actual == MaterialStrength.Clear;

		// 低渲染能力会忽略用户选择强制实色，这里如实说明，不假装切成功了。
		int tier = System.Windows.Media.RenderCapability.Tier >> 16;
		MaterialMenu.ToolTip = tier < 2 || SystemParameters.HighContrast
			? $"当前使用实色材质（渲染档位 {tier}；高对比度：{SystemParameters.HighContrast}）。已保留偏好：{UiAppearance.DisplayName}。"
			: $"玻璃材质强度：实色 / 着色玻璃（默认）/ 通透。当前：{UiAppearance.DisplayName}。纯外观设置，不影响任何功能。";
	}


	public void ApplyLicenseStatus(LicenseStatus status)
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.Invoke(() =>
			{
				ApplyLicenseStatus(status);
			});
		}
		else
		{
			_viewModel.UpdateLicenseStatus(status);
		}
	}

	private void License_StatusChanged(object? sender, LicenseStatus status)
	{
		ApplyLicenseStatus(status);
		if (_bridge != null)
		{
			RefreshRuntimeEntitlementAsync(_bridge, status.IsSponsor);
		}
	}

	private static async Task RefreshRuntimeEntitlementAsync(RuntimeBridgeClient bridge, bool sponsor)
	{
		try
		{
			await bridge.UpdateEntitlementAsync(sponsor, CancellationToken.None);
		}
		catch
		{
		}
	}

	private void SponsorGate_ActivationRequested(object? sender, SponsorAccessRequestedEventArgs e)
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.BeginInvoke(new Action(OpenActivationWindow));
		}
		else
		{
			OpenActivationWindow();
		}
	}

	private void LicenseBadge_Click(object sender, RoutedEventArgs e)
	{
		OpenActivationWindow();
	}

	private void OpenActivationWindow()
	{
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			if (e.ClickCount == 2)
			{
				ToggleMaximize();
			}
			else
			{
				DragMove();
			}
		}
	}

	private void Minimize_Click(object sender, RoutedEventArgs e)
	{
		WindowState = WindowState.Minimized;
	}

	private void Maximize_Click(object sender, RoutedEventArgs e)
	{
		ToggleMaximize();
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ToggleMaximize()
	{
		WindowState = ((WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		nint handle = new WindowInteropHelper(this).Handle;
		HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
		if (!NativeHotkeys.RegisterHotKey(handle, 4740166, 6u, 123u))
		{
			_viewModel.ModeStatus = "紧急停用快捷键 Ctrl+Shift+F12 注册失败";
		}
		// 材质菜单勾选状态要与启动时实际生效的档位一致。
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(SyncMaterialMenu));
	}

	private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
	{
		if (message == 786 && wParam == 4740166)
		{
			handled = true;
			_ = _viewModel.EmergencyDisableAsync(_bridge);
		}
		return 0;
	}

	private void PollGame(object? sender, EventArgs e)
	{
		ProcessLocateResult processLocateResult = HowToFishProcessLocator.FindAnyRunning();
		MainViewModel viewModel = _viewModel;
		string connectionStatus;
		if (processLocateResult.IsSuccess)
		{
			connectionStatus = $"已发现游戏 · PID {processLocateResult.Target.ProcessId}";
		}
		else
		{
			connectionStatus = (processLocateResult.AccessDenied ? "发现游戏但无法读取进程信息" : "等待 How to Fish");
		}
		viewModel.ConnectionStatus = connectionStatus;
		if (!processLocateResult.IsSuccess)
		{
			_viewModel.ModeStatus = (processLocateResult.AccessDenied ? "进程信息访问被拒绝 · 未执行附加" : "未连接 · 只读保护");
			_attachPolicy.Reset();
			if (_bridge != null && !_attaching)
			{
				DisconnectBridgeAsync(_bridge);
			}
			return;
		}
		try
		{
			if (_bridge != null && _attachedProcessId != processLocateResult.Target.ProcessId)
			{
				DisconnectBridgeAsync(_bridge);
				return;
			}
			string directoryName = Path.GetDirectoryName(processLocateResult.Target.ExecutablePath);
			GameFingerprint observed = GameFingerprintReader.Read(directoryName);
			CompatibilityReport compatibilityReport = EvaluateCompatibility(directoryName, observed);
			_viewModel.ModeStatus = CompatibilityPresentation.Status(compatibilityReport);
			if (_compatibilityNotice.ShouldShow(observed, compatibilityReport))
			{
				MessageBox.Show(this, CompatibilityPresentation.UpdateMessage(observed), "游戏版本更新提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			if (!compatibilityReport.CanWrite)
			{
				if (_bridge != null)
				{
					DisconnectBridgeAsync(_bridge);
				}
			}
			else if (compatibilityReport.CanWrite && _bridge == null && !_attaching)
			{
				if (_attachPolicy.TryBegin(processLocateResult.Target.ProcessId))
				{
					AttachBridgeAsync(processLocateResult.Target.ProcessId, directoryName);
				}
				else
				{
					_viewModel.ModeStatus = _attachPolicy.DescribeRetry(processLocateResult.Target.ProcessId);
				}
			}
			else if (compatibilityReport.CanWrite && _bridge != null && _attachedProcessId == processLocateResult.Target.ProcessId && !_refreshingMode)
			{
				RefreshSessionModeAsync(_bridge);
			}
		}
		catch (Exception ex)
		{
			_viewModel.ModeStatus = "校验失败 · 已切换只读 · " + ex.GetType().Name;
			_viewModel.SuspendConnection();
			if (_bridge != null)
			{
				DisconnectBridgeAsync(_bridge);
			}
		}
	}

	private CompatibilityReport EvaluateCompatibility(string gameRoot, GameFingerprint observed)
	{
		string text = $"{observed.UnityVersion}|{observed.SteamBuildId}|{observed.AssemblySha256}|{observed.AssemblyMvid:D}|{observed.Is64Bit}";
		if (_compatibilityCacheKey == text && (object)_compatibilityCache != null)
		{
			return _compatibilityCache;
		}
		GameContractReport contracts = (GameBuildCatalog.Certified.Any((GameFingerprint build) => CompatibilityEngine.Compare(build, observed).Status == CompatibilityStatus.Supported) ? GameContractReport.Compatible : GameContractValidator.Validate(gameRoot));
		_compatibilityCache = CompatibilityEngine.Evaluate(GameBuildCatalog.Certified, observed, contracts);
		_compatibilityCacheKey = text;
		return _compatibilityCache;
	}

	private async Task AttachBridgeAsync(int processId, string gameRoot)
	{
        if (_appearanceClosing) return;
        RuntimeBridgeClient? connected = null;
		_attaching = true;
		try
		{
            connected = await RuntimeBridgeClient.ConnectAsync(processId, gameRoot, CancellationToken.None, _license.Current.IsSponsor);
            if (_appearanceClosing) { await connected.DisposeAsync(); return; }
            _bridge = connected;
			_licensedRuntime = new LicensedRuntimeClient(_bridge, _sponsorGate);
			RuntimeSessionSnapshot snapshot;
			try
			{
				snapshot = await _bridge.InspectSessionAsync(CancellationToken.None);
			}
			catch (Exception innerException)
			{
				throw new RuntimeBridgeConnectException(RuntimeBridgeConnectStage.SessionHandshake, innerException);
			}
            if (!IsCurrentBridge(connected)) return;
			await ApplySessionModeAsync(connected, snapshot);
            if (!IsCurrentBridge(connected)) return;
			_attachedProcessId = processId;
			_attachPolicy.RecordSuccess(processId);
		}
		catch (Exception exception)
		{
            if (_appearanceClosing || (connected != null && !IsCurrentBridge(connected)))
            { if (connected != null) await connected.DisposeAsync(); return; }
			string text = BridgeConnectionDiagnostics.Describe(exception);
			_attachPolicy.RecordFailure(processId, text);
			TryLogBridgeFailure(processId, exception);
			_viewModel.ModeStatus = "运行时桥失败 · " + text;
			if (_bridge != null)
			{
				await _bridge.DisposeAsync();
			}
			_bridge = null;
			_licensedRuntime = null;
			_attachedProcessId = null;
			_sessionMode = null;
			_sceneHandle = null;
			_viewModel.SuspendConnection();
		}
		finally
		{
			_attaching = false;
		}
	}

    private bool IsCurrentBridge(RuntimeBridgeClient bridge) => !_appearanceClosing && ReferenceEquals(_bridge, bridge);

	private async Task RefreshSessionModeAsync(RuntimeBridgeClient bridge)
	{
        if (!IsCurrentBridge(bridge)) return;
		_refreshingMode = true;
		try
		{
            string? inspectedGullRound = _viewModel.GullRoundForInspection;
            var snapshot = await bridge.InspectSessionAsync(CancellationToken.None);
            if (!IsCurrentBridge(bridge)) return;
			await ApplySessionModeAsync(bridge, snapshot);
            if (!IsCurrentBridge(bridge)) return;
            _viewModel.UpdateExperienceSession(bridge.LastInspection, inspectedGullRound);
			await _viewModel.RefreshRuntimeProofsAsync();
		}
		catch (Exception innerException)
		{
            if (!IsCurrentBridge(bridge)) { await bridge.DisposeAsync(); return; }
			RuntimeBridgeConnectException exception = new RuntimeBridgeConnectException(RuntimeBridgeConnectStage.SessionRefresh, innerException);
			string text = BridgeConnectionDiagnostics.Describe(exception);
			int? attachedProcessId = _attachedProcessId;
			if (attachedProcessId.HasValue)
			{
				int valueOrDefault = attachedProcessId.GetValueOrDefault();
				_attachPolicy.RecordFailure(valueOrDefault, text);
				TryLogBridgeFailure(valueOrDefault, exception);
			}
			_viewModel.ModeStatus = "运行时桥已失去响应 · 已切换只读 · " + text;
			_viewModel.SuspendConnection();
			await bridge.DisposeAsync();
			_bridge = null;
			_licensedRuntime = null;
			_attachedProcessId = null;
			_sessionMode = null;
			_sceneHandle = null;
		}
		finally
		{
			_refreshingMode = false;
		}
	}

	private static void TryLogBridgeFailure(int processId, Exception exception)
	{
		try
		{
			BridgeConnectionDiagnostics.Append(Path.Combine(ProductIdentity.AppDataDirectory, "Logs"), processId, exception);
		}
		catch
		{
		}
	}

	private async Task DisconnectBridgeAsync(RuntimeBridgeClient bridge)
	{
		if (_bridge == bridge)
		{
			_viewModel.Disconnect();
			_bridge = null;
			_licensedRuntime = null;
			_attachedProcessId = null;
			_sessionMode = null;
			_sceneHandle = null;
			_consentSession = null;
			_consentLobbyId = null;
			_consentOwnerId = null;
			_consentGranted = false;
			await bridge.DisposeAsync();
		}
	}

	private async Task ApplySessionModeAsync(RuntimeBridgeClient bridge, RuntimeSessionSnapshot snapshot)
	{
        if (!IsCurrentBridge(bridge)) return;
		SessionMode mode = RuntimeSessionMode.From(snapshot.Host, snapshot.Client, snapshot.Lobby);
		bool accessChanged = mode != _sessionMode;
		ulong? sceneHandle = _sceneHandle;
		int num;
		if (sceneHandle.HasValue)
		{
			ulong valueOrDefault = sceneHandle.GetValueOrDefault();
			num = ((valueOrDefault != snapshot.SceneHandle) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool sceneChanged = (byte)num != 0;
		bool identityChanged = mode == SessionMode.JoinedFriend && (_consentSession != bridge.SessionId || !string.Equals(_consentLobbyId, snapshot.LobbyId, StringComparison.Ordinal) || _consentOwnerId != snapshot.OwnerId);
		if (accessChanged | identityChanged)
		{
            // Stop accepting old-session aim requests before the asynchronous clear.
            // Otherwise a click while awaiting disable_all can queue an enable after it.
            _viewModel.AimAssist.Disconnect();
			SessionMode? sessionMode = _sessionMode;
			if (sessionMode.HasValue)
			{
				await bridge.ExecuteAsync(new FeatureCommand("locks.disable_all", JsonSerializer.SerializeToElement(new { }), Capability.OwnedPlayer), CancellationToken.None);
                if (!IsCurrentBridge(bridge)) return;
			}
		}
		if (mode != SessionMode.JoinedFriend)
		{
			_consentSession = null;
			_consentLobbyId = null;
			_consentOwnerId = null;
			_consentGranted = false;
		}
		else if (identityChanged)
		{
			_consentSession = bridge.SessionId;
			_consentLobbyId = snapshot.LobbyId;
			_consentOwnerId = snapshot.OwnerId;
			_consentGranted = true;
			accessChanged = true;
		}
		CapabilitySet capabilitySet = CapabilityMatrix.For(mode, _consentGranted);
		if (accessChanged)
		{
			if (_licensedRuntime == null)
			{
				_licensedRuntime = new LicensedRuntimeClient(bridge, _sponsorGate);
			}
			_viewModel.Connect(_licensedRuntime, capabilitySet);
		}
		if (sceneChanged)
		{
			_viewModel.ResetWorldContext();
			_viewModel.RefreshCatalogAsync();
			if (capabilitySet.CanExecute(Capability.HostWorld))
			{
				try
				{
					RuntimeCommandResult runtimeCommandResult = await bridge.ExecuteAsync(new FeatureCommand("fun.event.reset_world", JsonSerializer.SerializeToElement(new { }), Capability.HostWorld), CancellationToken.None);
                    if (!IsCurrentBridge(bridge)) return;
					if (!runtimeCommandResult.Succeeded)
					{
						_viewModel.PositionStatus = "世界切换清理失败 · " + runtimeCommandResult.Message;
					}
					else
					{
						JsonElement? applied = runtimeCommandResult.Applied;
						if (applied.HasValue && applied.GetValueOrDefault().TryGetProperty("totalFunEntities", out var value) && value.TryGetInt32(out var value2) && value2 > 0)
						{
							_viewModel.PositionStatus = $"世界已切换；仍有 {value2} 个趣味实体未清理，请再次点击“清理全部趣味实体”";
						}
					}
				}
				catch (Exception ex)
				{
                    if (!IsCurrentBridge(bridge)) return;
					_viewModel.PositionStatus = "世界切换清理失败 · " + ex.Message;
				}
				_viewModel.FunEventsPage.RefreshAvailabilityAsync();
			}
		}
		_sessionMode = mode;
		_sceneHandle = snapshot.SceneHandle;
		MainViewModel viewModel = _viewModel;
		viewModel.ModeStatus = mode switch
		{
			SessionMode.Host => "运行时桥已连接 · 房主模式 · 当前玩家与世界功能可用", 
			SessionMode.Solo => "运行时桥已连接 · 单人模式 · 当前玩家与世界功能可用", 
			SessionMode.JoinedFriend => (!_consentGranted) ? "运行时桥已连接 · 好友房 · 功能可调用" : "运行时桥已连接 · 好友房 · 功能可调用", 
			SessionMode.MainMenu => "运行时桥已连接 · 请先进入房间", 
			_ => "运行时桥已连接 · 请先进入房间", 
		};
	}

	protected override void OnClosed(EventArgs e)
	{
        _appearanceClosing = true;
        _fishingJournal?.Dispose();
        CloseExperienceUi();
        _viewModel.ClosePreferences();
		// 释放页面切换动画留在内容区上的动画时钟。
		// 背景（研究探针实测）：Storyboard / 子时间线上的 FillBehavior="Stop" 只让属性值回到
		// 基值，并不保证时钟被摘掉——动画结束后 HasAnimatedProperties 仍可能为 True。
		// 官方推荐的做法是显式 BeginAnimation(prop, null)，这里在窗口关闭时对长寿命宿主执行，
		// 避免窗口在关闭过程中仍被动画时钟持有。只动 UI 动画属性，不触及任何业务状态。
		ReleaseContentHostAnimationClocks();
		_crosshairOverlay.Dispose();
		_timer.Stop();
		_license.StatusChanged -= License_StatusChanged;
		_sponsorGate.ActivationRequested -= SponsorGate_ActivationRequested;
		NativeHotkeys.UnregisterHotKey(new WindowInteropHelper(this).Handle, 4740166);
		if (_bridge != null)
		{
			_bridge.DisposeAsync().AsTask().GetAwaiter()
				.GetResult();
		}
		if (_ownsLicense)
		{
			_license.Dispose();
		}
		base.OnClosed(e);
	}

	// 供上面的关闭路径调用：把内容宿主上的 Opacity / TranslateTransform / ScaleTransform 动画，
	// 以及 SolidColorBrush.Color 上的颜色动画，显式摘掉。
	// 背景（研究探针 + 独立诊断实测）：Storyboard / 子时间线上的 FillBehavior="Stop" 只让属性值
	// 回到基值，并不保证时钟被摘掉——动画结束后 HasAnimatedProperties 仍可能为 True。
	// 官方推荐的做法是显式 BeginAnimation(prop, null)。这是长寿命宿主，所以在这里释放。
	//
	// 为什么整个循环包在 try/catch 里：独立诊断抓到一个只在关闭时出现的崩溃——
	// "无法在 TranslateTransform 上激活 Y 属性，因为该对象已密封或已冻结"。
	// 说明"已密封"与 IsFrozen 不是同一件事，属性检查覆盖不全；而窗口关闭路径
	// 绝不能因为一次外观层清理而抛异常，所以以异常兜底为准，IsFrozen 只作为快速跳过。
	private void ReleaseContentHostAnimationClocks()
	{
		try
		{
			foreach (FrameworkElement host in EnumerateVisualTree<FrameworkElement>(this))
			{
				if (host.HasAnimatedProperties)
				{
					host.BeginAnimation(OpacityProperty, null);
				}
				ReleaseTransformAnimationClocks(host.RenderTransform);
				ReleaseBrushAnimationClocks(host);
			}
		}
		catch (InvalidOperationException)
		{
			// 某个 Freezable 处于"已密封"状态，无法解绑动画。保留动画时钟不影响进程退出，
			// 这里只是尽力释放，不能影响关闭流程。
		}
	}

	// RenderTransform 可能是单个 Translate/Scale，也可能是 TransformGroup（例如浮动选中胶囊）。
	// 只解绑动画属性，不写任何基值——基值在动画开始前已经写好，回落是安全的。
	private static void ReleaseTransformAnimationClocks(Transform? transform)
	{
		switch (transform)
		{
			case TranslateTransform translate when !translate.IsFrozen && translate.HasAnimatedProperties:
				translate.BeginAnimation(TranslateTransform.XProperty, null);
				translate.BeginAnimation(TranslateTransform.YProperty, null);
				break;
			case ScaleTransform scale when !scale.IsFrozen && scale.HasAnimatedProperties:
				scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
				scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
				break;
			case TransformGroup group when !group.IsFrozen:
				foreach (Transform child in group.Children)
				{
					ReleaseTransformAnimationClocks(child);
				}
				break;
		}
	}

	// NavListItem 选中态会把 ContentPresenter 的 TextElement.Foreground 画刷做成 ColorAnimation
	// （见 themes/theme.xaml）。这些 SolidColorBrush 是模板内联实例、未冻结，动画结束后同样可能
	// 仍挂着时钟，一并摘掉；顺带覆盖常见的 Foreground/Background/BorderBrush 承载属性。
	private static void ReleaseBrushAnimationClocks(DependencyObject host)
	{
		ReleaseBrushAnimationClock(host, System.Windows.Documents.TextElement.ForegroundProperty);
		if (host is Control control)
		{
			ReleaseBrushAnimationClock(control, Control.ForegroundProperty);
			ReleaseBrushAnimationClock(control, Control.BackgroundProperty);
			ReleaseBrushAnimationClock(control, Control.BorderBrushProperty);
		}
		else if (host is Border border)
		{
			ReleaseBrushAnimationClock(border, Border.BackgroundProperty);
			ReleaseBrushAnimationClock(border, Border.BorderBrushProperty);
		}
		else if (host is Panel panel)
		{
			ReleaseBrushAnimationClock(panel, Panel.BackgroundProperty);
		}
		else if (host is TextBlock textBlock)
		{
			ReleaseBrushAnimationClock(textBlock, TextBlock.ForegroundProperty);
		}
	}

	private static void ReleaseBrushAnimationClock(DependencyObject owner, DependencyProperty brushProperty)
	{
		if (owner.GetValue(brushProperty) is SolidColorBrush brush && !brush.IsFrozen && brush.HasAnimatedProperties)
		{
			brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
		}
	}

	private static IEnumerable<T> EnumerateVisualTree<T>(DependencyObject root) where T : DependencyObject
	{
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int index = 0; index < count; index++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, index);
			if (child is T typed)
			{
				yield return typed;
			}
			foreach (T nested in EnumerateVisualTree<T>(child))
			{
				yield return nested;
			}
		}
	}
}
