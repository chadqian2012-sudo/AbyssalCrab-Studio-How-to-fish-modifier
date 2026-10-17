using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HowToFishTrainer.App.Licensing;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
	public const string ConsentWarning = "请不要在未经房主及其他玩家同意的房间中随意使用修改器。高级功能可能影响其他玩家体验及房主世界进度。";

	public const string ConnectionWarning = "连接建议：请尽量不要使用管理员方式启动游戏；游戏与修改器权限级别不一致可能导致连接失败。";

	private static readonly HashSet<string> TeleportCustomFeatureIds = new HashSet<string>(StringComparer.Ordinal) { "world.position.read", "world.teleport", "world.island.load" };

	private static readonly HashSet<string> CombatCustomFeatureIds = new HashSet<string>(StringComparer.Ordinal) { "combat.aim_assist" };

	private NavigationPage _selectedPage;

	private string _connectionStatus = "等待 How to Fish";

	private string _modeStatus = "未连接 · 只读保护";

	private IGameRuntimeClient? _runtime;

	private CapabilitySet _capabilities = CapabilitySet.Empty;

	private IReadOnlyList<CatalogOption> _itemCatalog = Array.Empty<CatalogOption>();

	private IReadOnlyList<CatalogOption> _fishCatalog = Array.Empty<CatalogOption>();

	private readonly Dictionary<string, FeatureToggleState> _toggleStates = new Dictionary<string, FeatureToggleState>(StringComparer.Ordinal);

	private string _catalogStatus = "等待连接后读取实时目录";

	private CancellationTokenSource? _catalogRetry;

	private int _connectionEpoch;
    private int _featureActionEpoch;

	private readonly TrainerSettingsStore _settings;

	private readonly IToggleSoundPlayer _soundPlayer;

	private readonly Action<string, string>? _showFeatureNotice;

	private readonly ISponsorAccessGate _sponsorAccessGate;

	private bool _soundEnabled = true;

	private string _licenseBadgeText = "标准版 · 点击激活";

	private string _licenseBadgeTitle = "标准版";

	private string _licenseBadgeSubtitle = "点击进入授权中心";

	private string _licenseBadgeDetail = "物品与鱼类生成需要赞助版";

	private bool _isSponsorActive;

	private TrainerTeleportPoint? _selectedTeleportPoint;
    private bool _updatingTeleportFilter;

	private TrainerTeleportPoint? _previousPosition;

	private readonly Dictionary<string, TrainerTeleportPoint> _previousPositions = new Dictionary<string, TrainerTeleportPoint>(StringComparer.Ordinal);

	private bool _targetRoomMember;

	private string _teleportPointQuery = string.Empty;

	private string _pointGroup = "默认分组";

	private string _pointName = string.Empty;

	private string _selectedTeleportGroup = "全部分组";

	private IslandCatalogOption? _selectedIsland;

	private string _islandQuery = string.Empty;

	private string _islandStatus = "等待连接后读取实时岛屿目录";

	private bool _islandLoadBusy;

	private int _islandCatalogEpoch;

	private bool _safeLanding = true;

	private DateTimeOffset _positionReadAt;

	private string _positionStatus = "尚未读取当前位置";

	private int _proofRefreshBusy;

	public IReadOnlyList<NavigationPage> Pages { get; }

	public ObservableCollection<FeatureItemViewModel> Features { get; }

	public NavigationPage SelectedPage
	{
		get
		{
			return _selectedPage;
		}
		set
		{
			if (Set(ref _selectedPage, value, "SelectedPage"))
			{
				RefreshFeatures();
				OnPropertyChanged("IsCombatPage");
				OnPropertyChanged("IsTeleportPage");
				OnPropertyChanged("IsFunEventsPage");
				OnPropertyChanged("IsEntertainmentPage");
				OnPropertyChanged("IsRoomMembersPage");
				OnPropertyChanged("IsCatalogPage");
				OnPropertyChanged("IsItemGeneratorPage");
				OnPropertyChanged("IsFishGeneratorPage");
				OnPropertyChanged("ActiveGenerator");
				if (!_restoringPreferences && (IsFunEventsPage || IsEntertainmentPage))
				{
					FunEventsPage.RefreshAvailabilityAsync();
				}
				if (!_restoringPreferences && IsRoomMembersPage)
				{
					RoomMembersPage.RefreshAsync();
				}
			}
		}
	}

	public string ConnectionStatus
	{
		get
		{
			return _connectionStatus;
		}
		set
		{
			Set(ref _connectionStatus, value, "ConnectionStatus");
		}
	}

	public string ModeStatus
	{
		get
		{
			return string.IsNullOrEmpty(_preferencesWarning) ? _modeStatus : _modeStatus + " · " + _preferencesWarning;
		}
		set
		{
			Set(ref _modeStatus, value, "ModeStatus");
		}
	}

	public string LicenseBadgeText
	{
		get
		{
			return _licenseBadgeText;
		}
		private set
		{
			Set(ref _licenseBadgeText, value, "LicenseBadgeText");
		}
	}

	public string LicenseBadgeTitle
	{
		get
		{
			return _licenseBadgeTitle;
		}
		private set
		{
			Set(ref _licenseBadgeTitle, value, "LicenseBadgeTitle");
		}
	}

	public string LicenseBadgeSubtitle
	{
		get
		{
			return _licenseBadgeSubtitle;
		}
		private set
		{
			Set(ref _licenseBadgeSubtitle, value, "LicenseBadgeSubtitle");
		}
	}

	public string LicenseBadgeDetail
	{
		get
		{
			return _licenseBadgeDetail;
		}
		private set
		{
			Set(ref _licenseBadgeDetail, value, "LicenseBadgeDetail");
		}
	}

	public bool IsSponsorActive
	{
		get
		{
			return _isSponsorActive;
		}
		private set
		{
			Set(ref _isSponsorActive, value, "IsSponsorActive");
		}
	}

	public string PageTitle => SelectedPage.Title;

	public bool IsCombatPage => SelectedPage.Category == "战斗";

	public bool IsTeleportPage => SelectedPage.Category == "世界传送";

	public bool IsEntertainmentPage => SelectedPage.Category == "幸运娱乐";

	public bool IsFunEventsPage => SelectedPage.Category == "趣味小功能";

	public bool IsRoomMembersPage => SelectedPage.Category == "房间成员";

	public bool IsCatalogPage
	{
		get
		{
			string category = SelectedPage.Category;
			if (category == "物品生成" || category == "鱼类生成")
			{
				return true;
			}
			return false;
		}
	}

	public bool IsItemGeneratorPage => SelectedPage.Category == "物品生成";

	public bool IsFishGeneratorPage => SelectedPage.Category == "鱼类生成";

	public FeatureItemViewModel? ActiveGenerator => Features.FirstOrDefault((FeatureItemViewModel feature) => feature.Descriptor.Control == FeatureControl.Generator);

	public string CatalogStatus
	{
		get
		{
			return _catalogStatus;
		}
		private set
		{
			Set(ref _catalogStatus, value, "CatalogStatus");
		}
	}

	public bool SoundEnabled
	{
		get
		{
			return _soundEnabled;
		}
		set
		{
			Set(ref _soundEnabled, value, "SoundEnabled");
		}
	}

	public ObservableCollection<TrainerTeleportPoint> TeleportPoints { get; }

	public ObservableCollection<TrainerTeleportPoint> FilteredTeleportPoints { get; }

	public ObservableCollection<string> TeleportGroups { get; }

	public ObservableCollection<IslandCatalogOption> IslandOptions { get; }

	public ObservableCollection<IslandCatalogOption> FilteredIslandOptions { get; }

	public IslandCatalogOption? SelectedIsland
	{
		get
		{
			return _selectedIsland;
		}
		set
		{
			if (Set(ref _selectedIsland, value, "SelectedIsland"))
			{
				OnPropertyChanged("CanLoadSelectedIsland");
			}
		}
	}

	public string IslandQuery
	{
		get
		{
			return _islandQuery;
		}
		set
		{
			if (Set(ref _islandQuery, value, "IslandQuery"))
			{
				RefreshIslandFilter();
			}
		}
	}

	public string IslandStatus
	{
		get
		{
			return _islandStatus;
		}
		private set
		{
			Set(ref _islandStatus, value, "IslandStatus");
		}
	}

	public bool CanLoadSelectedIsland
	{
		get
		{
			if (!_islandLoadBusy)
			{
				return SelectedIsland?.Available ?? false;
			}
			return false;
		}
	}

	public TrainerTeleportPoint? SelectedTeleportPoint
	{
		get
		{
			return _selectedTeleportPoint;
		}
		set
		{
			if (Set(ref _selectedTeleportPoint, value, "SelectedTeleportPoint") && (object)value != null && !_updatingTeleportFilter)
			{
				CurrentX = value.X;
				CurrentY = value.Y;
				CurrentZ = value.Z;
				PointGroup = value.Group;
				PointName = value.Name;
				LoadPointDetails();
			}
		}
	}

	public string TeleportPointQuery
	{
		get
		{
			return _teleportPointQuery;
		}
		set
		{
			if (Set(ref _teleportPointQuery, value, "TeleportPointQuery"))
			{
				RefreshTeleportFilter();
			}
		}
	}

	public string SelectedTeleportGroup
	{
		get
		{
			return _selectedTeleportGroup;
		}
		set
		{
			if (Set(ref _selectedTeleportGroup, value, "SelectedTeleportGroup"))
			{
				RefreshTeleportFilter();
			}
		}
	}

	public string PointGroup
	{
		get
		{
			return _pointGroup;
		}
		set
		{
			Set(ref _pointGroup, value, "PointGroup");
		}
	}

	public string PointName
	{
		get
		{
			return _pointName;
		}
		set
		{
			Set(ref _pointName, value, "PointName");
		}
	}

    public NumericInput CurrentXInput { get; } = new("X 坐标", 0, -100000, 100000);
    public NumericInput CurrentYInput { get; } = new("Y 坐标", 0, -100000, 100000);
    public NumericInput CurrentZInput { get; } = new("Z 坐标", 0, -100000, 100000);
	public double CurrentX
	{
		get
		{
			return CurrentXInput.Value;
		}
		set
		{
			CurrentXInput.Value = value;
			OnPropertyChanged(nameof(CurrentX));
		}
	}

	public double CurrentY
	{
		get
		{
			return CurrentYInput.Value;
		}
		set
		{
			CurrentYInput.Value = value;
			OnPropertyChanged(nameof(CurrentY));
		}
	}

	public double CurrentZ
	{
		get
		{
			return CurrentZInput.Value;
		}
		set
		{
			CurrentZInput.Value = value;
			OnPropertyChanged(nameof(CurrentZ));
		}
	}

	public string PositionStatus
	{
		get
		{
			return _positionStatus;
		}
		set
		{
			Set(ref _positionStatus, value, "PositionStatus");
		}
	}

	public bool SafeLanding
	{
		get
		{
			return _safeLanding;
		}
		set
		{
			Set(ref _safeLanding, value, "SafeLanding");
		}
	}

	public bool TargetRoomMember
	{
		get
		{
			return _targetRoomMember;
		}
		set
		{
			Set(ref _targetRoomMember, value && CanTargetRoomMember, "TargetRoomMember");
		}
	}

	public bool CanTargetRoomMember => _capabilities.CanExecute(Capability.HostWorld);

	public RoomMemberSnapshot? SelectedTeleportMember
	{
		get
		{
			return RoomMembersPage.SelectedMember;
		}
		set
		{
			RoomMembersPage.SelectedMember = value;
			OnPropertyChanged("SelectedTeleportMember");
		}
	}

	public ICommand ReadPositionCommand { get; }

	public ICommand TeleportSelectedCommand { get; }

	public ICommand ReturnPreviousCommand { get; }

	public ICommand SavePointCommand { get; }

	public ICommand DeletePointCommand { get; }

	public ICommand LoadSelectedIslandCommand { get; }

	public ICommand RefreshCatalogCommand { get; }

	public FunEventsViewModel FunEventsPage { get; }

	public RoomMembersViewModel RoomMembersPage { get; }

	public AimAssistViewModel AimAssist { get; }

	public CrosshairViewModel Crosshair { get; } = new CrosshairViewModel();

	public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(IToggleSoundPlayer? soundPlayer = null, ISponsorAccessGate? sponsorAccessGate = null, Action<string, string>? showFeatureNotice = null, IUserPreferencesStore? preferencesStore = null, string? experiencePath = null)
	{
		_showFeatureNotice = showFeatureNotice;
        _settings = new TrainerSettingsStore(experiencePath == null ? System.IO.Path.Combine(ProductIdentity.AppDataDirectory,"settings.json") : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(experiencePath))!,"teleport-points.json"));
		_soundPlayer = soundPlayer ?? new ToggleSoundPlayer();
		_sponsorAccessGate = sponsorAccessGate ?? AllowAllSponsorAccessGate.Instance;
		Pages = new[]
		{
			new NavigationPage("玩家状态","玩家","🎮"),
			new NavigationPage("武器战斗","战斗","⚔"),
			new NavigationPage("钓鱼辅助","钓鱼","🎣"),
			new NavigationPage("金钱背包","金钱背包","◇"),
			new NavigationPage("物品生成","物品生成","◈"),
			new NavigationPage("鱼类生成","鱼类生成","🐟"),
			new NavigationPage("世界传送","世界传送","⌖"),
			new NavigationPage("房间成员","房间成员","♙"),
			new NavigationPage("解锁存档","解锁中心","✦"),
			new NavigationPage("娱乐控制","幸运娱乐","◎"),
			new NavigationPage("趣味事件","趣味小功能","✧"),
		};
		FunEventsPage = new FunEventsViewModel(_sponsorAccessGate);
		RoomMembersPage = new RoomMembersViewModel(OpenRoomMemberInTeleport, _sponsorAccessGate);
		AimAssist = new AimAssistViewModel();
		_selectedPage = Pages[0];
		Features = new ObservableCollection<FeatureItemViewModel>();
		TeleportPoints = new ObservableCollection<TrainerTeleportPoint>(_settings.Load());
		FilteredTeleportPoints = new ObservableCollection<TrainerTeleportPoint>(TeleportPoints);
		TeleportGroups = new ObservableCollection<string>();
		IslandOptions = new ObservableCollection<IslandCatalogOption>();
		FilteredIslandOptions = new ObservableCollection<IslandCatalogOption>();
		RefreshTeleportGroups();
		ReadPositionCommand = new RelayCommand(() =>
		{
			_ = RunPositionActionAsync(ReadPositionAsync);
		});
		TeleportSelectedCommand = new RelayCommand(() =>
		{
			_ = RunPositionActionAsync(TeleportSelectedAsync);
		});
		ReturnPreviousCommand = new RelayCommand(() =>
		{
			_ = RunPositionActionAsync(ReturnPreviousAsync);
		});
		SavePointCommand = new RelayCommand(SavePoint);
		DeletePointCommand = new RelayCommand(DeletePoint);
		LoadSelectedIslandCommand = new RelayCommand(() =>
		{
			LoadSelectedIslandAsync();
		});
		RefreshCatalogCommand = new RelayCommand(() =>
		{
			RefreshCatalogAsync();
		});
		RefreshFeatures();
		InitializePreferences(preferencesStore);
		InitializeExperience(experiencePath ?? (preferencesStore is FileUserPreferencesStore && !Environment.GetCommandLineArgs().Contains("--ui-preview",StringComparer.OrdinalIgnoreCase) ? System.IO.Path.Combine(ProductIdentity.AppDataDirectory,"experience-settings.json") : null));
	}

	private void OpenRoomMemberInTeleport(RoomMemberSnapshot member)
	{
		RoomMembersPage.SelectedMember = member;
		OnPropertyChanged("SelectedTeleportMember");
		TargetRoomMember = true;
		SelectedPage = Pages.Single((NavigationPage page) => page.Category == "世界传送");
	}

	public void Connect(IGameRuntimeClient runtime, CapabilitySet capabilities)
	{
        ResetExperienceWorld();
        _featureActionEpoch++;
		int connectionEpoch = Interlocked.Increment(ref _connectionEpoch);
		CancellationToken cancellationToken = ReplaceCatalogRetry();
		_runtime = runtime;
		_capabilities = capabilities;
		FunEventsPage.Connect(runtime, capabilities);
		RoomMembersPage.Connect(runtime, capabilities);
		AimAssist.Connect(runtime, capabilities);
		Crosshair.Connect(runtime);
		RefreshFeatures();
		ReplayEnabledTogglesAsync(runtime, capabilities, connectionEpoch);
		if (capabilities.CanExecute(Capability.HostWorld))
		{
			LoadCatalogWithRetryAsync(runtime, cancellationToken);
		}
		LoadIslandCatalogAsync(runtime, connectionEpoch);
		if (_sponsorAccessGate.IsSponsor && capabilities.CanExecute(Capability.HostWorld))
		{
			RoomMembersPage.RefreshAsync();
		}
		OnPropertyChanged("CanLoadSelectedIsland");
		OnPropertyChanged("CanTargetRoomMember");
	}

	public void SuspendConnection(string message = "运行时桥暂时失联，正在自动重连")
	{
		DetachRuntime(message, preserveToggleIntent: true);
	}

	public void Disconnect()
	{
		DetachRuntime("游戏连接已断开", preserveToggleIntent: false);
	}

	private void DetachRuntime(string message, bool preserveToggleIntent)
	{
        _featureActionEpoch++;
		StopExperienceAutomation();
		CombatDiagnostics = "运行时已断开 · 诊断读数已失效";
		Interlocked.Increment(ref _connectionEpoch);
		Interlocked.Increment(ref _islandCatalogEpoch);
		CancelCatalogRetry();
		FunEventsPage.Disconnect();
		RoomMembersPage.Disconnect();
		AimAssist.Disconnect();
		Crosshair.Disconnect();
		ResetWorldContext(message);
		_runtime = null;
		_capabilities = CapabilitySet.Empty;
		_itemCatalog = Array.Empty<CatalogOption>();
		_fishCatalog = Array.Empty<CatalogOption>();
		if (!preserveToggleIntent)
		{
			_toggleStates.Clear();
		}
		IslandOptions.Clear();
		FilteredIslandOptions.Clear();
		SelectedIsland = null;
		IslandStatus = "等待连接后读取实时岛屿目录";
		CatalogStatus = (preserveToggleIntent ? "运行时桥暂时失联，重连后自动恢复持续功能" : "等待连接后读取实时目录");
		RefreshFeatures();
		OnPropertyChanged("CanLoadSelectedIsland");
		TargetRoomMember = false;
		OnPropertyChanged("CanTargetRoomMember");
	}

	public void ResetWorldContext(string message = "世界已切换，请重新读取当前位置")
	{
		ResetExperienceWorld();
		_previousPosition = null;
		_previousPositions.Clear();
		_positionReadAt = default;
		PositionStatus = message;
	}

	public void MarkAllLocksDisabled(string message)
	{
		StopExperienceAutomation();
        AimAssist.Disconnect();
        if(_runtime!=null)AimAssist.Connect(_runtime,_capabilities);
		Crosshair.Enabled = false;
		string[] array = _toggleStates.Keys.ToArray();
		foreach (string key in array)
		{
			_toggleStates[key] = _toggleStates[key] with { Enabled = false, Status = message, StateLabel = "已关闭", IsSuccessful = true };
		}
		foreach (FeatureItemViewModel item in Features.Where((FeatureItemViewModel item) => item.Descriptor.Control == FeatureControl.Toggle))
		{
			item.ApplyExternalToggleState(item.CaptureToggleState() with { Enabled = false, Status = message, StateLabel = "已关闭", IsSuccessful = true });
		}
		OnPropertyChanged(nameof(ActiveFeaturesText));
	}

	public async Task RefreshRuntimeProofsAsync()
	{
		IGameRuntimeClient runtime = _runtime;
		if (runtime == null || Interlocked.Exchange(ref _proofRefreshBusy, 1) != 0)
		{
			return;
		}
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(new FeatureCommand("features.proofs", JsonSerializer.SerializeToElement(new { }), Capability.ReadOnly), CancellationToken.None);
			if (!runtimeCommandResult.Succeeded)
			{
				return;
			}
			JsonElement? applied = runtimeCommandResult.Applied;
			if (!applied.HasValue)
			{
				return;
			}
			JsonElement valueOrDefault = applied.GetValueOrDefault();
			if (valueOrDefault.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			foreach (JsonElement item in valueOrDefault.EnumerateArray())
			{
				string text = Text(item, "featureId");
				if (text != null && _toggleStates.TryGetValue(text, out FeatureToggleState value) && value.Enabled)
				{
					string text2 = Text(item, "state") ?? "waiting";
					string status = Text(item, "reason") ?? string.Empty;
					FeatureToggleState featureToggleState;
					if (text2 == "applied")
					{
						featureToggleState = new FeatureToggleState(Enabled: true, status, "已验证", IsSuccessful: true);
					}
					else
					{
						featureToggleState = ((!(text2 == "failed")) ? new FeatureToggleState(Enabled: true, status, "等待触发", IsSuccessful: false) : new FeatureToggleState(Enabled: true, status, "执行失败", IsSuccessful: false));
					}
					FeatureToggleState state = featureToggleState;
					ApplyRememberedToggleState(text, state);
				}
			}
		}
		catch
		{
		}
		finally
		{
			Volatile.Write(ref _proofRefreshBusy, 0);
		}
	}

	public static string DisplayCategory(string id, string originalCategory)
    {
        if (id == "persistence.save" || id == "inventory.unlock_all_slots") return "解锁中心";
        if (id == "fishing.skin_jackpot" || id == "fun.slot_machine.rarity") return "幸运娱乐";
        return originalCategory;
    }

	private void RefreshFeatures()
	{
        CaptureFeatureParameters();
        DetachFeatureParameterObservers();
        _refreshingFeatureParameters = true;
		Features.Clear();
		foreach (FeatureDescriptor item in FeatureRegistry.All.Where((FeatureDescriptor x) => DisplayCategory(x.Id, x.Category) == SelectedPage.Category && x.Id != "fishing.skin_jackpot" && x.Id != "fun.slot_machine.rarity" && (!IsTeleportPage || !TeleportCustomFeatureIds.Contains(x.Id)) && (!IsCombatPage || !CombatCustomFeatureIds.Contains(x.Id))))
		{
			IReadOnlyList<CatalogOption> readOnlyList2;
			if (!(item.Id == "generation.fish"))
			{
				if (!(item.Id == "generation.item"))
				{
					IReadOnlyList<CatalogOption> readOnlyList = Array.Empty<CatalogOption>();
					readOnlyList2 = readOnlyList;
				}
				else
				{
					readOnlyList2 = _itemCatalog;
				}
			}
			else
			{
				readOnlyList2 = _fishCatalog;
			}
			IReadOnlyList<CatalogOption> options = readOnlyList2;
            int actionEpoch=_featureActionEpoch;
			_toggleStates.TryGetValue(item.Id, out FeatureToggleState value);
            var feature = new FeatureItemViewModel(item, _runtime, _capabilities, options, value, source=>{if(actionEpoch==_featureActionEpoch)RememberToggleState(source);}, PlayEnabledSound, _sponsorAccessGate, _showFeatureNotice);
            AttachExperience(feature);
            RestoreFeatureParameters(feature);
            Features.Add(feature);
            ObserveFeatureParameters(feature);
		}
        _refreshingFeatureParameters = false;
		OnPropertyChanged("PageTitle");
		OnPropertyChanged("ActiveGenerator");
		OnPropertyChanged(nameof(FeelFeatures));
		OnPropertyChanged(nameof(HasFeelFeatures));
		OnPropertyChanged(nameof(DisplayFeatures));
	}

	public void RefreshSponsorAccess()
	{
		foreach (FeatureItemViewModel feature in Features)
		{
			feature.RefreshSponsorAccess();
		}
		RoomMembersPage.RefreshSponsorAccess();
	}

	public void UpdateLicenseStatus(LicenseStatus status)
	{
		IsSponsorActive = status.IsSponsor;
		LicenseBadgeTitle = (status.IsSponsor ? "功能已就绪" : "标准版");
		LicenseBadgeSubtitle = status.State switch
		{
			LicenseState.SponsorActive => "授权已生效", 
			LicenseState.OfflineGrace => "离线授权有效", 
			LicenseState.Invalid => "点击查看授权状态", 
			LicenseState.DeviceLimit => "点击管理授权设备", 
			LicenseState.ServiceUnavailable => "服务暂不可用", 
			_ => "点击进入授权中心", 
		};
		LicenseBadgeText = status.State switch
		{
			LicenseState.SponsorActive => "全部功能已开放", 
			LicenseState.OfflineGrace => "赞助版 · 离线授权", 
			LicenseState.Invalid => "授权未通过 · 点击查看", 
			LicenseState.DeviceLimit => "设备已达上限 · 点击查看", 
			LicenseState.ServiceUnavailable => "授权服务暂不可用", 
			_ => "标准版 · 点击激活", 
		};
		LicenseBadgeDetail = status.Message;
		RefreshSponsorAccess();
	}

	private void PlayEnabledSound()
	{
		if (SoundEnabled)
		{
			_soundPlayer.PlayEnabled();
		}
	}

	private void RememberToggleState(FeatureItemViewModel source)
	{
		if (source.Descriptor.Control == FeatureControl.Toggle)
		{
			FeatureToggleState featureToggleState = source.CaptureToggleState();
			_toggleStates[source.Descriptor.Id] = featureToggleState;
			OnPropertyChanged(nameof(ActiveFeaturesText));
			Features.FirstOrDefault((FeatureItemViewModel item) => item.Descriptor.Id == source.Descriptor.Id && item != source)?.ApplyExternalToggleState(featureToggleState);
		}
	}

	private async Task ReplayEnabledTogglesAsync(IGameRuntimeClient runtime, CapabilitySet capabilities, int connectionEpoch)
	{
		KeyValuePair<string, FeatureToggleState>[] array = _toggleStates.Where((KeyValuePair<string, FeatureToggleState> keyValuePair) => keyValuePair.Value.Enabled).ToArray();
		KeyValuePair<string, FeatureToggleState>[] array2 = array;
		for (int num = 0; num < array2.Length; num++)
		{
			KeyValuePair<string, FeatureToggleState> pair = array2[num];
			if (!ReplayIsCurrent(runtime, connectionEpoch))
			{
				break;
			}
			if (!_toggleStates.TryGetValue(pair.Key, out FeatureToggleState value) || !value.Enabled)
			{
				continue;
			}
			FeatureDescriptor featureDescriptor = FeatureRegistry.Find(pair.Key);
			if (ClientProtectionNotice.IsRestricted(pair.Key, capabilities))
			{
				ApplyRememberedToggleState(pair.Key, ClientProtectionNotice.DisabledState);
				continue;
			}
			if ((object)featureDescriptor == null || featureDescriptor.Control != FeatureControl.Toggle || !RuntimeFeatureSupport.ImplementedIds.Contains(featureDescriptor.Id) || !capabilities.CanExecute(featureDescriptor.RequiredCapability))
			{
				ApplyRememberedToggleState(pair.Key, new FeatureToggleState(Enabled: false, "会话已变化，当前权限不允许恢复此功能", "已关闭", IsSuccessful: false));
				continue;
			}
			try
			{
				RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(new FeatureCommand(featureDescriptor.Id,
                    featureDescriptor.Id == "player.spin.lock" ? FeatureItemViewModel.SpinArguments(true, value.AppliedSpinSpeed) : JsonSerializer.SerializeToElement(new
				{
					enabled = true
				}), featureDescriptor.RequiredCapability), CancellationToken.None);
				if (!ReplayIsCurrent(runtime, connectionEpoch))
				{
					break;
				}
				if (!_toggleStates.TryGetValue(pair.Key, out value) || !value.Enabled)
				{
					continue;
				}
				int num2;
				if (runtimeCommandResult.Succeeded)
				{
					JsonElement? applied = runtimeCommandResult.Applied;
					if (applied.HasValue)
					{
						JsonElement valueOrDefault = applied.GetValueOrDefault();
						if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("applied", out var value2))
						{
							num2 = ((value2.ValueKind == JsonValueKind.False) ? 1 : 0);
							goto IL_026c;
						}
					}
				}
				num2 = 0;
				goto IL_026c;
				IL_026c:
				bool flag = (byte)num2 != 0;
				string stateLabel;
				if (runtimeCommandResult.Succeeded)
				{
					stateLabel = (flag ? "等待条件" : "已开启");
				}
				else
				{
					stateLabel = "恢复失败";
				}
				ApplyRememberedToggleState(pair.Key, new FeatureToggleState(runtimeCommandResult.Succeeded, runtimeCommandResult.Message, stateLabel, runtimeCommandResult.Succeeded));
			}
			catch (Exception ex)
			{
				if (!ReplayIsCurrent(runtime, connectionEpoch))
				{
					break;
				}
				if (_toggleStates.TryGetValue(pair.Key, out value) && value.Enabled)
				{
					ApplyRememberedToggleState(pair.Key, new FeatureToggleState(Enabled: false, "自动恢复失败 · " + ex.Message, "恢复失败", IsSuccessful: false));
				}
			}
		}
	}

	private bool ReplayIsCurrent(IGameRuntimeClient runtime, int connectionEpoch)
	{
		if (connectionEpoch == Volatile.Read(in _connectionEpoch))
		{
			return _runtime == runtime;
		}
		return false;
	}

	private void ApplyRememberedToggleState(string id, FeatureToggleState state)
	{
        if (id == "player.spin.lock" && _toggleStates.TryGetValue(id, out var previous))
            state = state with { SpinSpeedText = previous.SpinSpeedText, AppliedSpinSpeed = previous.AppliedSpinSpeed };
		_toggleStates[id] = state;
		Features.FirstOrDefault((FeatureItemViewModel feature) => feature.Descriptor.Id == id)?.ApplyExternalToggleState(state);
	}

	public async Task RefreshCatalogAsync()
	{
		if (_runtime == null)
		{
			CatalogStatus = "请先连接游戏";
			return;
		}
		IGameRuntimeClient runtime = _runtime;
		int connectionEpoch = Volatile.Read(in _connectionEpoch);
		await LoadIslandCatalogAsync(runtime, connectionEpoch);
		if (!_capabilities.CanExecute(Capability.HostWorld))
		{
			CatalogStatus = "岛屿目录可浏览；当前会话不允许生成物品";
			return;
		}
		CancellationToken cancellationToken = ReplaceCatalogRetry();
		await LoadCatalogAsync(runtime, cancellationToken);
	}

	private async Task LoadIslandCatalogAsync(IGameRuntimeClient runtime, int connectionEpoch)
	{
		int requestEpoch = Interlocked.Increment(ref _islandCatalogEpoch);
		IslandStatus = "正在读取游戏岛屿目录…";
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(new FeatureCommand("catalog.islands", JsonSerializer.SerializeToElement(new { }), Capability.ReadOnly), CancellationToken.None);
			if (!IslandRequestIsCurrent(runtime, connectionEpoch, requestEpoch))
			{
				return;
			}
			if (!runtimeCommandResult.Succeeded)
			{
				IslandStatus = "岛屿目录读取失败：" + runtimeCommandResult.Message;
				return;
			}
			JsonElement? applied = runtimeCommandResult.Applied;
			if (applied.HasValue)
			{
				JsonElement valueOrDefault = applied.GetValueOrDefault();
				if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("islands", out var value) && value.ValueKind == JsonValueKind.Array)
				{
					string selectedKey = _preferredIslandKey ?? SelectedIsland?.Key;
					IslandCatalogOption[] array = (from @group in (from jsonElement in value.EnumerateArray()
							where jsonElement.ValueKind == JsonValueKind.Object && ((jsonElement.TryGetProperty("index", out var value2) && value2.TryGetInt32(out var value3) && value3 >= 0 && value3 <= 255) || (jsonElement.TryGetProperty("sceneBuildIndex", out var value4) && value4.TryGetInt32(out var value5) && value5 >= 0 && value5 <= 255))
							select jsonElement).Select((JsonElement element) =>
						{
							JsonElement value4;
							int num2;
							if (!element.TryGetProperty("sceneBuildIndex", out var value2) || !value2.TryGetInt32(out var value3))
							{
								value4 = element.GetProperty("index");
								num2 = value4.GetInt32() + 1;
							}
							else
							{
								num2 = value3;
							}
							int num3 = num2;
							int? num4 = ((element.TryGetProperty("runtimeIndex", out var value5) && value5.ValueKind == JsonValueKind.Number && value5.TryGetInt32(out var value6)) ? new int?(value6) : (element.TryGetProperty("runtimeIndex", out value4) ? ((int?)null) : ((element.TryGetProperty("index", out var value7) && value7.TryGetInt32(out var value8)) ? new int?(value8) : ((int?)null))));
							int num5 = ((element.TryGetProperty("index", out var value9) && value9.TryGetInt32(out var value10)) ? value10 : (num4 ?? num3));
							string key = Text(element, "key")?.Trim() ?? $"island:{num5}";
							string rawName = Text(element, "rawName")?.Trim() ?? string.Empty;
							string localizedName = Text(element, "localizedName");
							bool flag = Boolean(element, "available");
							return new IslandCatalogOption(Reason: Text(element, "reason")?.Trim() ?? (flag ? "可载入" : "当前不可载入"), LoadMode: Text(element, "loadMode")?.Trim() ?? ((!num4.HasValue) ? "unavailable" : "native-island"), Experimental: Boolean(element, "experimental"), Key: key, Index: num5, RuntimeIndex: num4, SceneBuildIndex: num3, Name: IslandNameLocalizer.Localize(num5, localizedName, rawName), RawName: rawName, Available: flag);
						}).GroupBy((IslandCatalogOption islandCatalogOption) => islandCatalogOption.Key, StringComparer.Ordinal)
						select @group.First() into islandCatalogOption
						orderby islandCatalogOption.SceneBuildIndex
						select islandCatalogOption).ToArray();
					IslandOptions.Clear();
					IslandCatalogOption[] array2 = array;
					foreach (IslandCatalogOption item in array2)
					{
						IslandOptions.Add(item);
					}
					RefreshIslandFilter();
					SelectedIsland = selectedKey != null ? IslandOptions.FirstOrDefault(option => option.Key == selectedKey) : IslandOptions.FirstOrDefault(option => option.Available) ?? IslandOptions.FirstOrDefault();
					IslandStatus = ((array.Length == 0) ? "岛屿目录为空；请进入世界后重新读取" : $"已读取 {array.Length} 个岛屿");
					return;
				}
			}
			IslandStatus = "岛屿目录读取失败：运行时返回格式无效";
		}
		catch (Exception ex)
		{
			if (IslandRequestIsCurrent(runtime, connectionEpoch, requestEpoch))
			{
				IslandStatus = "岛屿目录读取失败：" + ex.Message;
			}
		}
	}

	private bool IslandRequestIsCurrent(IGameRuntimeClient runtime, int connectionEpoch, int requestEpoch)
	{
		if (requestEpoch == Volatile.Read(in _islandCatalogEpoch) && connectionEpoch == Volatile.Read(in _connectionEpoch))
		{
			return _runtime == runtime;
		}
		return false;
	}

	private void RefreshIslandFilter()
	{
		string query = IslandQuery.Trim();
		FilteredIslandOptions.Clear();
		foreach (IslandCatalogOption item in IslandOptions.Where((IslandCatalogOption option) => query.Length == 0 || option.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || option.RawName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || option.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || option.SceneBuildIndex.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) || option.Index.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)))
		{
			FilteredIslandOptions.Add(item);
		}
	}

	private async Task LoadSelectedIslandAsync()
	{
		if (_islandLoadBusy)
		{
			return;
		}
		if (_runtime == null)
		{
			PositionStatus = "请先连接游戏";
			return;
		}
		if (!_capabilities.CanExecute(Capability.HostWorld))
		{
			PositionStatus = "岛屿载入只能由房主或单人世界执行";
			return;
		}
		IslandCatalogOption selectedIsland = SelectedIsland;
		if ((object)selectedIsland == null)
		{
			PositionStatus = "请先选择岛屿";
			return;
		}
		if (!selectedIsland.Available)
		{
			PositionStatus = selectedIsland.Reason;
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("world.island.load"))
		{
			PositionStatus = "此操作需要激活赞助版；未向游戏发送命令。";
			return;
		}
		_islandLoadBusy = true;
		OnPropertyChanged("CanLoadSelectedIsland");
		try
		{
			int? runtimeIndex = selectedIsland.RuntimeIndex;
			JsonElement jsonElement;
			if (runtimeIndex.HasValue)
			{
				int valueOrDefault = runtimeIndex.GetValueOrDefault();
				jsonElement = JsonSerializer.SerializeToElement(new
				{
					key = selectedIsland.Key,
					runtimeIndex = valueOrDefault,
					sceneBuildIndex = selectedIsland.SceneBuildIndex,
					value = valueOrDefault
				});
			}
			else
			{
				jsonElement = JsonSerializer.SerializeToElement(new
				{
					key = selectedIsland.Key,
					runtimeIndex = (int?)null,
					sceneBuildIndex = selectedIsland.SceneBuildIndex
				});
			}
			JsonElement arguments = jsonElement;
			PositionStatus = (await new FeatureEngine(_runtime).ExecuteAsync(new FeatureCommand("world.island.load", arguments, Capability.HostWorld), _capabilities, CancellationToken.None)).Message;
		}
		catch (Exception ex)
		{
			PositionStatus = "岛屿载入失败 · " + ex.Message;
		}
		finally
		{
			_islandLoadBusy = false;
			OnPropertyChanged("CanLoadSelectedIsland");
		}
	}

	private CancellationToken ReplaceCatalogRetry()
	{
		CancelCatalogRetry();
		_catalogRetry = new CancellationTokenSource();
		return _catalogRetry.Token;
	}

	private void CancelCatalogRetry()
	{
		CancellationTokenSource catalogRetry = _catalogRetry;
		_catalogRetry = null;
		if (catalogRetry != null)
		{
			catalogRetry.Cancel();
			catalogRetry.Dispose();
		}
	}

	private async Task LoadCatalogWithRetryAsync(IGameRuntimeClient runtime, CancellationToken cancellationToken)
	{
		for (int attempt = 1; attempt <= 5; attempt++)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			if (_runtime != runtime)
			{
				break;
			}
			if (await LoadCatalogAsync(runtime, cancellationToken))
			{
				break;
			}
			if (attempt < 5)
			{
				try
				{
					await Task.Delay(500, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
			}
		}
	}

	private async Task<bool> LoadCatalogAsync(IGameRuntimeClient runtime, CancellationToken cancellationToken)
	{
		CatalogStatus = "正在读取游戏预制体目录…";
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(new FeatureCommand("catalog.items", JsonSerializer.SerializeToElement(new { }), Capability.ReadOnly), CancellationToken.None);
			if (cancellationToken.IsCancellationRequested || _runtime != runtime)
			{
				return false;
			}
			if (!runtimeCommandResult.Succeeded)
			{
				CatalogStatus = "目录读取失败：" + runtimeCommandResult.Message;
				return false;
			}
			JsonElement? applied = runtimeCommandResult.Applied;
			if (applied.HasValue)
			{
				JsonElement valueOrDefault = applied.GetValueOrDefault();
				JsonElement element = default;
				JsonElement jsonElement;
				if (valueOrDefault.ValueKind == JsonValueKind.Array)
				{
					jsonElement = valueOrDefault;
				}
				else
				{
					if (valueOrDefault.ValueKind != JsonValueKind.Object || !valueOrDefault.TryGetProperty("items", out var value) || value.ValueKind != JsonValueKind.Array)
					{
						CatalogStatus = "目录读取失败：运行时返回格式无效";
						return false;
					}
					jsonElement = value;
					if (valueOrDefault.TryGetProperty("diagnostics", out var value2) && value2.ValueKind == JsonValueKind.Object)
					{
						element = value2;
					}
				}
				CatalogOption[] array = (from item in (from item in jsonElement.EnumerateArray()
						where item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var value3) && value3.TryGetInt32(out var value4) && value4 >= 0 && value4 <= 255
						select item).Select((JsonElement item) =>
					{
						int @int = item.GetProperty("id").GetInt32();
						string name = ((item.TryGetProperty("name", out var value3) && value3.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value3.GetString())) ? value3.GetString() : $"物品 {@int}");
						bool flag = item.TryGetProperty("fish", out var value4);
						if (flag)
						{
							JsonValueKind valueKind = value4.ValueKind;
							bool flag2 = valueKind - 5 <= JsonValueKind.Object;
							flag = flag2;
						}
						bool flag3 = flag && value4.GetBoolean();
						string kind = Text(item, "kind") ?? (flag3 ? "鱼类" : "其他");
						int worth = Integer(item, "worth");
						JsonElement element2 = ((item.TryGetProperty("weapon", out var value5) && value5.ValueKind == JsonValueKind.Object) ? value5 : default(JsonElement));
						double damage = Number(element2, "damage");
						double interval = Number(element2, "fireInterval");
						double speed = Number(element2, "projectileSpeed");
						double spread = Number(element2, "spread");
						double recoil = Number(element2, "recoil");
						double magazine = Number(element2, "magazine");
						JsonElement element3 = ((item.TryGetProperty("attachments", out var value6) && value6.ValueKind == JsonValueKind.Object) ? value6 : default(JsonElement));
						IReadOnlyList<SkinOption> skinOptions = SkinOptions(item);
						return new CatalogOption(@int, name, flag3, kind, worth, damage, PerformanceStats(damage, interval, speed, spread, recoil, magazine), AttachmentOptions(element3, "sights"), AttachmentOptions(element3, "barrels"), AttachmentOptions(element3, "ammo"), Boolean(element3, "extendedMag"), Boolean(element3, "laser"), skinOptions);
					})
					group item by item.Id into @group
					select @group.First() into item
					orderby item.Name
					select item).ToArray();
				_itemCatalog = array.Where((CatalogOption item) => !item.IsFish).ToArray();
				_fishCatalog = array.Where((CatalogOption item) => item.IsFish).ToArray();
				if (array.Length == 0)
				{
					CatalogStatus = "目录为空；请进入世界后点击重新读取";
				}
				else
				{
					CatalogStatus = $"已读取 {array.Length} 个物品，其中 {_fishCatalog.Count} 个鱼类";
					if (element.ValueKind == JsonValueKind.Object)
					{
						CatalogStatus += $" · 权威目录 {Integer(element, "authoritative")} · 来源 {Integer(element, "sources")} · 重复ID {Integer(element, "duplicateIds")}";
					}
				}
				RefreshFeatures();
				return array.Length != 0;
			}
			CatalogStatus = "目录读取失败：运行时返回格式无效";
			return false;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception ex2)
		{
			if (!cancellationToken.IsCancellationRequested && _runtime == runtime)
			{
				CatalogStatus = "目录读取失败：" + ex2.Message;
			}
			return false;
		}
	}

	private static string? Text(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static int Integer(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var value2))
		{
			return 0;
		}
		return value2;
	}

	private static double Number(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetDouble(out var value2))
		{
			return 0.0;
		}
		return value2;
	}

	private static bool Boolean(JsonElement element, string name)
	{
		JsonElement value = default;
		bool flag = element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
		if (flag)
		{
			JsonValueKind valueKind = value.ValueKind;
			bool flag2 = valueKind - 5 <= JsonValueKind.Object;
			flag = flag2;
		}
		if (flag)
		{
			return value.GetBoolean();
		}
		return false;
	}

	private static IReadOnlyList<AttachmentOption> AttachmentOptions(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<AttachmentOption>();
		}
		return (from element2 in value.EnumerateArray()
			where element2.ValueKind == JsonValueKind.Object
			select new AttachmentOption(Text(element2, "id") ?? Text(element2, "name") ?? string.Empty, Text(element2, "name") ?? "未命名配件") into attachmentOption
			where attachmentOption.Id.Length > 0
			select attachmentOption).ToArray();
	}

	private static IReadOnlyList<SkinOption> SkinOptions(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("skins", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<SkinOption>();
		}
		return (from element2 in value.EnumerateArray()
			where element2.ValueKind == JsonValueKind.Object
			select new SkinOption(Text(element2, "id") ?? string.Empty, Text(element2, "name") ?? "未命名皮肤", Text(element2, "rarity") ?? string.Empty) into skinOption
			where skinOption.Id.Length > 0
			select skinOption).ToArray();
	}

	private static IReadOnlyList<PerformanceStat> PerformanceStats(double damage, double interval, double speed, double spread, double recoil, double magazine)
	{
		return new _003C_003Ez__ReadOnlyArray<PerformanceStat>(new PerformanceStat[6]
		{
			new PerformanceStat("伤害", damage.ToString("0.##"), Math.Clamp(damage / 100.0, 0.0, 1.0)),
			new PerformanceStat("射速", (interval <= 0.0) ? "—" : ((1.0 / interval).ToString("0.##") + "/秒"), (interval <= 0.0) ? 0.0 : Math.Clamp(1.0 / interval / 15.0, 0.0, 1.0)),
			new PerformanceStat("精准度", spread.ToString("0.##"), Math.Clamp(1.0 - spread / 12.0, 0.0, 1.0)),
			new PerformanceStat("后坐力控制", recoil.ToString("0.##"), Math.Clamp(1.0 - recoil / 20.0, 0.0, 1.0)),
			new PerformanceStat("弹速", speed.ToString("0.##"), Math.Clamp(speed / 150.0, 0.0, 1.0)),
			new PerformanceStat("弹匣", magazine.ToString("0"), Math.Clamp(magazine / 50.0, 0.0, 1.0))
		});
	}

	private async Task ReadPositionAsync()
	{
		if (_runtime == null)
		{
			PositionStatus = "请先连接游戏";
			return;
		}
		try
		{
			RoomMemberSnapshot member = (TargetRoomMember ? SelectedTeleportMember : null);
			RuntimeCommandResult runtimeCommandResult = (((object)member == null || !CanTargetRoomMember) ? (await _runtime.ExecuteAsync(new FeatureCommand("world.position.read", JsonSerializer.SerializeToElement(new { }), Capability.ReadOnly), CancellationToken.None)) : (await _runtime.ExecuteAsync(new FeatureCommand("players.position.read", JsonSerializer.SerializeToElement(new
			{
				playerKey = member.Key
			}), Capability.HostWorld), CancellationToken.None)));
			RuntimeCommandResult runtimeCommandResult2 = runtimeCommandResult;
			if (runtimeCommandResult2.Succeeded)
			{
				JsonElement? applied = runtimeCommandResult2.Applied;
				if (applied.HasValue)
				{
					JsonElement valueOrDefault = applied.GetValueOrDefault();
					CurrentX = valueOrDefault.GetProperty("x").GetDouble();
					CurrentY = valueOrDefault.GetProperty("y").GetDouble();
					CurrentZ = valueOrDefault.GetProperty("z").GetDouble();
					_positionReadAt = DateTimeOffset.UtcNow;
					PositionStatus = (((object)member == null) ? "当前玩家位置已读取" : (member.DisplayName + " 的位置已读取"));
					return;
				}
			}
			PositionStatus = runtimeCommandResult2.Message;
		}
		catch (Exception ex)
		{
			PositionStatus = ex.Message;
		}
	}

	private async Task TeleportSelectedAsync()
	{
		if (_runtime == null || !_capabilities.CanExecute(Capability.OwnedPlayer))
		{
			PositionStatus = "当前会话不允许传送";
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("world.teleport"))
		{
			PositionStatus = "此操作需要激活赞助版；未向游戏发送命令。";
			return;
		}
		TrainerTeleportPoint selectedTeleportPoint = SelectedTeleportPoint;
        IGameRuntimeClient runtime = _runtime;
        int epoch = _connectionEpoch;
        string coordinateError = NumericInput.FirstError(CurrentXInput, CurrentYInput, CurrentZInput);
        if (coordinateError.Length != 0)
        {
            PositionStatus = coordinateError;
            return;
        }
		double targetX = CurrentX;
		double targetY = CurrentY;
		double targetZ = CurrentZ;
		RoomMemberSnapshot member = (TargetRoomMember ? SelectedTeleportMember : null);
		string targetKey = member?.Key ?? "local";
		RuntimeCommandResult runtimeCommandResult = (((object)member == null || !CanTargetRoomMember) ? (await _runtime.ExecuteAsync(new FeatureCommand("world.position.read", JsonSerializer.SerializeToElement(new { }), Capability.ReadOnly), CancellationToken.None)) : (await _runtime.ExecuteAsync(new FeatureCommand("players.position.read", JsonSerializer.SerializeToElement(new
		{
			playerKey = member.Key
		}), Capability.HostWorld), CancellationToken.None)));
		RuntimeCommandResult runtimeCommandResult2 = runtimeCommandResult;
		if (runtime != _runtime || epoch != _connectionEpoch) { PositionStatus="会话已切换，本次传送已取消";return; }
		if (runtimeCommandResult2.Succeeded)
		{
			JsonElement? applied = runtimeCommandResult2.Applied;
			if (applied.HasValue)
			{
				JsonElement valueOrDefault = applied.GetValueOrDefault();
				_previousPosition = new TrainerTeleportPoint("临时", "上一次位置", valueOrDefault.GetProperty("x").GetDouble(), valueOrDefault.GetProperty("y").GetDouble(), valueOrDefault.GetProperty("z").GetDouble());
				_previousPositions[targetKey] = _previousPosition;
			}
		}
		CurrentX = targetX;
		CurrentY = targetY;
		CurrentZ = targetZ;
		if ((object)member != null && CanTargetRoomMember)
		{
			PositionStatus = (await _runtime.ExecuteAsync(new FeatureCommand("players.teleport", JsonSerializer.SerializeToElement(new
			{
				playerKey = member.Key,
				mode = "coordinates",
				x = targetX,
				y = targetY,
				z = targetZ
			}), Capability.HostWorld), CancellationToken.None)).Message;
		}
		else
		{
			PositionStatus = (await new FeatureEngine(_runtime).ExecuteAsync(new FeatureCommand("world.teleport", JsonSerializer.SerializeToElement(new
			{
				x = targetX,
				y = targetY,
				z = targetZ,
				safe = SafeLanding
			}), Capability.OwnedPlayer), _capabilities, CancellationToken.None)).Message;
		}
	}

	private async Task ReturnPreviousAsync()
	{
		RoomMemberSnapshot roomMemberSnapshot = (TargetRoomMember ? SelectedTeleportMember : null);
		string targetKey = roomMemberSnapshot?.Key ?? "local";
		TrainerTeleportPoint trainerTeleportPoint = (_previousPositions.TryGetValue(targetKey, out TrainerTeleportPoint value) ? value : _previousPosition);
		if (_runtime == null || (object)trainerTeleportPoint == null)
		{
			PositionStatus = "当前目标还没有可返回的位置";
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("world.teleport"))
		{
			PositionStatus = "此操作需要激活赞助版；未向游戏发送命令。";
			return;
		}
		TrainerTeleportPoint target = trainerTeleportPoint;
		bool flag;
		string message;
		if ((object)roomMemberSnapshot != null && CanTargetRoomMember)
		{
			RuntimeCommandResult runtimeCommandResult = await _runtime.ExecuteAsync(new FeatureCommand("players.teleport", JsonSerializer.SerializeToElement(new
			{
				playerKey = roomMemberSnapshot.Key,
				mode = "coordinates",
				x = target.X,
				y = target.Y,
				z = target.Z
			}), Capability.HostWorld), CancellationToken.None);
			flag = runtimeCommandResult.Succeeded;
			message = runtimeCommandResult.Message;
		}
		else
		{
			OperationReceipt operationReceipt = await new FeatureEngine(_runtime).ExecuteAsync(new FeatureCommand("world.teleport", JsonSerializer.SerializeToElement(new
			{
				x = target.X,
				y = target.Y,
				z = target.Z,
				safe = SafeLanding
			}), Capability.OwnedPlayer), _capabilities, CancellationToken.None);
			ReceiptStatus status = operationReceipt.Status;
			bool flag2 = (uint)status <= 1u;
			flag = flag2;
			message = operationReceipt.Message;
		}
		if (flag)
		{
			CurrentX = target.X;
			CurrentY = target.Y;
			CurrentZ = target.Z;
			_previousPositions.Remove(targetKey);
			if (targetKey == "local")
			{
				_previousPosition = null;
			}
		}
		PositionStatus = message;
	}

	private void SavePoint()
	{
        string error = NumericInput.FirstError(CurrentXInput, CurrentYInput, CurrentZInput);
        if (error.Length != 0) { PositionStatus = error; return; }
		if (SelectedTeleportPoint == null && DateTimeOffset.UtcNow - _positionReadAt > TimeSpan.FromSeconds(10.0))
		{
			PositionStatus = "请先重新读取当前位置再保存";
			return;
		}
		try
		{
			TrainerTeleportPoint point = new TrainerTeleportPoint(PointGroup.Trim(), PointName.Trim(), CurrentX, CurrentY, CurrentZ);
			TrainerTeleportPoint existing = TeleportPoints.FirstOrDefault((TrainerTeleportPoint item) => string.Equals(item.Group, point.Group, StringComparison.CurrentCultureIgnoreCase) && string.Equals(item.Name, point.Name, StringComparison.CurrentCultureIgnoreCase));
            if(existing != null && SelectedTeleportPoint != null && !ReferenceEquals(existing,SelectedTeleportPoint)) { PositionStatus="已有同名坐标，请换一个名称";return; }
            existing ??= SelectedTeleportPoint;
			var oldDecoration=existing==null?null:GetPointDecoration(existing);
			TrainerTeleportPoint[] points = TeleportPoints.Where((TrainerTeleportPoint item) => (object)item != existing).Append(point).ToArray();
			_settings.Save(points);
			if ((object)existing != null)
			{
				TeleportPoints.Remove(existing);
			}
			TeleportPoints.Add(point);
            if(oldDecoration!=null){_experience.Points.Remove(oldDecoration);_experience.Points.Add(oldDecoration with{Group=point.Group,Name=point.Name});ScheduleExperienceSave();PointMetadataChanged();}
			RefreshTeleportGroups();
			RefreshTeleportFilter();
			SelectedTeleportPoint = point;
			PositionStatus = "坐标已保存到修改器本地设置";
		}
		catch (Exception ex)
		{
			PositionStatus = "保存坐标失败 · " + ex.Message;
		}
	}

	private void DeletePoint()
	{
		if ((object)SelectedTeleportPoint == null)
		{
			return;
		}
		try
		{
			TrainerTeleportPoint selected = SelectedTeleportPoint;
			_settings.Save(TeleportPoints.Where((TrainerTeleportPoint point) => (object)point != selected));
			TeleportPoints.Remove(selected);
            _experience.Points.RemoveAll(p=>p.Group==selected.Group&&p.Name==selected.Name); ScheduleExperienceSave();PointMetadataChanged();
			RefreshTeleportGroups();
			RefreshTeleportFilter();
			SelectedTeleportPoint = null;
			PositionStatus = "坐标已删除";
		}
		catch (Exception ex)
		{
			PositionStatus = "删除坐标失败 · " + ex.Message;
		}
	}

	private void RefreshTeleportFilter()
	{
		string query = TeleportPointQuery.Trim();
        var selected=SelectedTeleportPoint;
        _updatingTeleportFilter=true;
        try{
            FilteredTeleportPoints.Clear();
            foreach (TrainerTeleportPoint item in SortPoints(TeleportPoints.Where((TrainerTeleportPoint point) => (SelectedTeleportGroup == "全部分组" || string.Equals(point.Group, SelectedTeleportGroup, StringComparison.CurrentCultureIgnoreCase)) && PointMatches(point,query))))
                FilteredTeleportPoints.Add(item);
            if(selected!=null && FilteredTeleportPoints.Contains(selected))SelectedTeleportPoint=selected;
        }finally{_updatingTeleportFilter=false;}
	}

	private void RefreshTeleportGroups()
	{
		string selectedTeleportGroup = SelectedTeleportGroup;
		TeleportGroups.Clear();
		TeleportGroups.Add("全部分组");
		foreach (string item in TeleportPoints.Select((TrainerTeleportPoint point) => point.Group).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string value) => value, StringComparer.CurrentCultureIgnoreCase))
		{
			TeleportGroups.Add(item);
		}
		SelectedTeleportGroup = (TeleportGroups.Contains(selectedTeleportGroup) ? selectedTeleportGroup : "全部分组");
	}

	private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		OnPropertyChanged(name);
		return true;
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
