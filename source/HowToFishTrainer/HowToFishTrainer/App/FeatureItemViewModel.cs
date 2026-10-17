using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HowToFishTrainer.App.Licensing;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class FeatureItemViewModel : INotifyPropertyChanged
{
	private readonly IGameRuntimeClient? _runtime;

	private readonly CapabilitySet? _capabilities;

	private readonly Action<FeatureItemViewModel>? _stateChanged;

	private readonly Action? _playEnabledSound;

	private readonly Action<string, string>? _showFeatureNotice;

	private readonly ISponsorAccessGate _sponsorAccessGate;

	private bool _enabled;

	private bool _saveAfter;

	private bool _deadFish;

	private bool _suppressToggleExecution;

	private bool _isBusy;

	private bool _isSuccessful;

	private double _value;
    private string _spinSpeedText = "4";
    private double _appliedSpinSpeed = 4d;
    private double _lastValidSpinSpeed = 4d;

    public bool IsSpin => Descriptor.Id == "player.spin.lock";
    public bool IsStandardToggle => !IsSpin;
    public bool HasSpinSpeedError => IsSpin && !TrySpinSpeed(SpinSpeedText, out _);
    public string SpinSpeedText
    {
        get => _spinSpeedText;
        set
        {
            _spinSpeedText = value ?? string.Empty;
            if (TrySpinSpeed(_spinSpeedText, out var validSpeed)) _lastValidSpinSpeed = validSpeed;
            Changed(nameof(SpinSpeedText));
            Changed(nameof(SpinSpeedValidationMessage));
            Changed(nameof(HasSpinSpeedError));
            Changed(nameof(CanApplySpinSpeed));
            NotifyStateChanged();
        }
    }
    public string SpinSpeedValidationMessage => IsSpin && !TrySpinSpeed(SpinSpeedText, out _) ? "请输入每秒 2–15 圈的转速" : string.Empty;
    public bool CanApplySpinSpeed => IsSpin && Enabled && CanInteract && TrySpinSpeed(SpinSpeedText, out _);
    public ICommand ApplySpinSpeedCommand { get; }
    public static bool TrySpinSpeed(string text, out double speed)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out speed) &&
            double.IsFinite(speed) && speed >= 2d && speed <= 15d;
    }
    public static JsonElement SpinArguments(bool enabled, double speed)
    {
        return enabled ? JsonSerializer.SerializeToElement(new { enabled, rotationsPerSecond = speed }) :
            JsonSerializer.SerializeToElement(new { enabled });
    }
    private double RequireSpinSpeed()
    {
        if (!TrySpinSpeed(SpinSpeedText, out double speed)) throw new ArgumentException("请输入每秒 2–15 圈的转速");
        return speed;
    }

	private double _distance;

	private bool _rare;

	private string _status;

	private string _stateLabel;

	private CatalogOption? _selectedOption;

	private AttachmentOption? _selectedSight;

	private AttachmentOption? _selectedBarrel;

	private AttachmentOption? _selectedAmmo;

	private SkinOption? _selectedSkin;

	private bool _extendedMagazine;

	private bool _laserSight;

	private string _searchText = string.Empty;

	private string _selectedKind = "全部";

	public FeatureDescriptor Descriptor { get; }

    public NumericInput ValueInput { get; }
    public NumericInput QuantityInput { get; }
    public NumericInput DistanceInput { get; }
    public NumericInput WeightInput { get; } = new("重量倍率", 1, 0.1, 20);
    public NumericInput XInput { get; } = new("X 坐标", 0, -100000, 100000);
    public NumericInput YInput { get; } = new("Y 坐标", 0, -100000, 100000);
    public NumericInput ZInput { get; } = new("Z 坐标", 0, -100000, 100000);
    public NumericInput[] EditableNumericInputs => Descriptor.Control switch
    {
        FeatureControl.Number => new[] { ValueInput },
        FeatureControl.Generator => new[] { QuantityInput, DistanceInput, WeightInput },
        FeatureControl.Coordinates => new[] { XInput, YInput, ZInput },
        _ => Array.Empty<NumericInput>()
    };

	public bool Enabled
	{
		get
		{
			return _enabled;
		}
		set
		{
			if (_enabled != value)
			{
				_enabled = value;
				Changed("Enabled");
                Changed(nameof(CanApplySpinSpeed));
				NotifyStateChanged();
				if (Descriptor.Control == FeatureControl.Toggle && !_suppressToggleExecution)
				{
					ExecuteToggleAsync(value);
				}
			}
		}
	}

	public bool SaveAfter
	{
		get
		{
			return _saveAfter;
		}
		set
		{
			_saveAfter = value;
			Changed("SaveAfter");
		}
	}

	public bool DeadFish
	{
		get
		{
			return _deadFish;
		}
		set
		{
			if (_deadFish != value)
			{
				_deadFish = value;
				Changed("DeadFish");
				Changed("AliveFish");
			}
		}
	}

	public bool AliveFish
	{
		get
		{
			return !DeadFish;
		}
		set
		{
			DeadFish = !value;
		}
	}

	public bool CanRequestNativeSave
	{
		get
		{
			bool flag = Descriptor.RequiredCapability == Capability.HostWorld && Descriptor.Id != "persistence.save";
			if (flag)
			{
				FeatureControl control = Descriptor.Control;
				bool flag2 = (uint)(control - 2) <= 1u;
				flag = flag2;
			}
			return flag;
		}
	}

	public double Value
	{
		get
		{
			return ValueInput.Value;
		}
		set
		{
			ValueInput.Value = value;
			Changed("Value");
		}
	}

	public int Quantity
	{
		get
		{
			return (int)QuantityInput.Value;
		}
		set
		{
			QuantityInput.Value = value;
			Changed("Quantity");
		}
	}

	public double Distance
	{
		get
		{
			return DistanceInput.Value;
		}
		set
		{
			DistanceInput.Value = value;
			Changed("Distance");
		}
	}

	public bool Rare
	{
		get
		{
			return _rare;
		}
		set
		{
			_rare = value;
			Changed("Rare");
		}
	}

	public double WeightMultiplier
	{
		get
		{
			return WeightInput.Value;
		}
		set
		{
			WeightInput.Value = value;
			Changed("WeightMultiplier");
		}
	}

	public IReadOnlyList<CatalogOption> Options { get; }

	public IReadOnlyList<string> Kinds
	{
		get
		{
			List<string> list = new List<string>();
			list.Add("全部");
			list.AddRange(from kind in Options.Select((CatalogOption option) => option.Kind).Distinct(StringComparer.CurrentCultureIgnoreCase)
				orderby kind
				select kind);
            if (Options.Count == 0 && !list.Contains(SelectedKind)) list.Add(SelectedKind);
			return new _003C_003Ez__ReadOnlyList<string>(list);
		}
	}

	public string SearchText
	{
		get
		{
			return _searchText;
		}
		set
		{
			if (!(_searchText == value))
			{
				_searchText = value ?? string.Empty;
				Changed("SearchText");
				Changed("FilteredOptions");
				SelectFirstFiltered();
			}
		}
	}

	public string SelectedKind
	{
		get
		{
			return _selectedKind;
		}
		set
		{
			if (!(_selectedKind == value))
			{
				_selectedKind = (string.IsNullOrWhiteSpace(value) ? "全部" : value);
				Changed("SelectedKind");
				Changed("FilteredOptions");
				SelectFirstFiltered();
			}
		}
	}

	public IReadOnlyList<CatalogOption> FilteredOptions => SortCatalogOptions(Options.Where((CatalogOption option) => MatchesCatalogView(option) && (SelectedKind == "全部" || string.Equals(option.Kind, SelectedKind, StringComparison.CurrentCultureIgnoreCase)) && (string.IsNullOrWhiteSpace(SearchText) || option.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase) || option.Id.ToString().Contains(SearchText.Trim(), StringComparison.Ordinal)))).ToArray();

	public bool ShowWeaponConfiguration
	{
		get
		{
			if (Descriptor.Id == "generation.item")
			{
				return SelectedOption?.IsWeapon ?? false;
			}
			return false;
		}
	}

	public bool ShowSkinConfiguration
	{
		get
		{
			if (Descriptor.Id == "generation.item")
			{
				return SelectedOption?.HasSkins ?? false;
			}
			return false;
		}
	}

	public bool ShowFishConfiguration => Descriptor.Id == "generation.fish";

	public bool RequiresSponsor => SponsorFeaturePolicy.RequiresSponsor(Descriptor.Id);

	public string ExecuteButtonText
	{
		get
		{
			if (!RequiresSponsor || _sponsorAccessGate.IsSponsor)
			{
				return "生成到眼前";
			}
			return "生成到眼前 · 赞助版";
		}
	}

	public CatalogOption? SelectedOption
	{
		get
		{
			return _selectedOption;
		}
		set
		{
			_selectedOption = value;
			if (!_restoringParameters && value != null) _pendingCatalogParameters = null;
			if ((object)value != null)
			{
				Value = value.Id;
			}
			_selectedSight = null;
			_selectedBarrel = null;
			_selectedAmmo = null;
			_selectedSkin = null;
			if ((object)value == null || !value.SupportsExtendedMagazine)
			{
				_extendedMagazine = false;
			}
			if ((object)value == null || !value.SupportsLaserSight)
			{
				_laserSight = false;
			}
			Changed("SelectedOption");
			Changed("ShowWeaponConfiguration");
			Changed("ShowSkinConfiguration");
			Changed("SelectedSight");
			Changed("SelectedBarrel");
			Changed("SelectedAmmo");
			Changed("SelectedSkin");
			Changed("ExtendedMagazine");
			Changed("LaserSight");
			Changed(nameof(IsFavorite));
			Changed(nameof(FavoriteButtonText));
		}
	}

	public AttachmentOption? SelectedSight
	{
		get
		{
			return _selectedSight;
		}
		set
		{
			RememberAttachmentSelection("sightId", value?.Id, _selectedSight?.Id);
			_selectedSight = value;
			Changed("SelectedSight");
		}
	}

	public AttachmentOption? SelectedBarrel
	{
		get
		{
			return _selectedBarrel;
		}
		set
		{
			RememberAttachmentSelection("barrelId", value?.Id, _selectedBarrel?.Id);
			_selectedBarrel = value;
			Changed("SelectedBarrel");
		}
	}

	public AttachmentOption? SelectedAmmo
	{
		get
		{
			return _selectedAmmo;
		}
		set
		{
			RememberAttachmentSelection("ammoId", value?.Id, _selectedAmmo?.Id);
			_selectedAmmo = value;
			Changed("SelectedAmmo");
		}
	}

	public SkinOption? SelectedSkin
	{
		get
		{
			return _selectedSkin;
		}
		set
		{
			RememberAttachmentSelection("skinId", value?.Id, _selectedSkin?.Id);
			_selectedSkin = value;
			Changed("SelectedSkin");
		}
	}

	public bool ExtendedMagazine
	{
		get
		{
			return _extendedMagazine;
		}
		set
		{
			_extendedMagazine = value && (SelectedOption?.SupportsExtendedMagazine ?? false);
			Changed("ExtendedMagazine");
		}
	}

	public bool LaserSight
	{
		get
		{
			return _laserSight;
		}
		set
		{
			_laserSight = value && (SelectedOption?.SupportsLaserSight ?? false);
			Changed("LaserSight");
		}
	}

	public double X
	{
		get
		{
			return XInput.Value;
		}
		set
		{
			XInput.Value = value;
			Changed("X");
		}
	}

	public double Y
	{
		get
		{
			return YInput.Value;
		}
		set
		{
			YInput.Value = value;
			Changed("Y");
		}
	}

	public double Z
	{
		get
		{
			return ZInput.Value;
		}
		set
		{
			ZInput.Value = value;
			Changed("Z");
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			_status = value;
			Changed("Status");
			NotifyStateChanged();
		}
	}

	public string StateLabel
	{
		get
		{
			return _stateLabel;
		}
		private set
		{
			_stateLabel = value;
			Changed("StateLabel");
			Changed("VisualState");
			NotifyStateChanged();
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		private set
		{
			if (_isBusy != value)
			{
				_isBusy = value;
				Changed("IsBusy");
				Changed("CanInteract");
                Changed(nameof(CanApplySpinSpeed));
				Changed("VisualState");
			}
		}
	}

	public bool IsSuccessful
	{
		get
		{
			return _isSuccessful;
		}
		private set
		{
			_isSuccessful = value;
			Changed("IsSuccessful");
			Changed("VisualState");
			NotifyStateChanged();
		}
	}

	public string VisualState
	{
		get
		{
			if (!IsBusy)
			{
				if (!StateLabel.Contains("失败", StringComparison.Ordinal))
				{
					if (!StateLabel.Contains("等待", StringComparison.Ordinal))
					{
						if (!IsSuccessful)
						{
							return "off";
						}
						return "applied";
					}
					return "waiting";
				}
				return "failed";
			}
			return "busy";
		}
	}

	public bool CanInteract
	{
		get
		{
			if (IsAvailable)
			{
				return !IsBusy && _executionBlockReason?.Invoke() == null;
			}
			return false;
		}
	}

	private bool IsClientProtectionRestricted => ClientProtectionNotice.IsRestricted(Descriptor.Id, _capabilities);

	public string CapabilityLabel => "实时控制";

	public ICommand ExecuteCommand { get; }

	public ICommand CleanupCommand { get; }

	public bool IsAvailable { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public FeatureItemViewModel(FeatureDescriptor descriptor, IGameRuntimeClient? runtime = null, CapabilitySet? capabilities = null, IReadOnlyList<CatalogOption>? options = null, FeatureToggleState? toggleState = null, Action<FeatureItemViewModel>? stateChanged = null, Action? playEnabledSound = null, ISponsorAccessGate? sponsorAccessGate = null, Action<string, string>? showFeatureNotice = null)
	{
		_showFeatureNotice = showFeatureNotice;
		Descriptor = descriptor;
		_runtime = runtime;
		_capabilities = capabilities;
		_stateChanged = stateChanged;
		_playEnabledSound = playEnabledSound;
		_sponsorAccessGate = sponsorAccessGate ?? AllowAllSponsorAccessGate.Instance;
		Options = options ?? Array.Empty<CatalogOption>();
		_value = ((double?)Options.FirstOrDefault()?.Id) ?? ((!descriptor.Minimum.HasValue) ? 1.0 : Math.Clamp(1.0, descriptor.Minimum.Value, descriptor.Maximum.Value));
		_selectedOption = Options.FirstOrDefault();
		_distance = ((descriptor.Id == "generation.fish") ? 3.5 : 2.5);
        bool integerValue = descriptor.Control == FeatureControl.Generator || descriptor.Id.StartsWith("money.", StringComparison.Ordinal)
            || descriptor.Id == "fun.slot_machine.rarity" || descriptor.Id == "world.island.load";
        ValueInput = new NumericInput(descriptor.Title, _value, descriptor.Minimum, descriptor.Maximum, integerValue);
        QuantityInput = new NumericInput("数量", 1, 1, descriptor.Id == "generation.fish" ? 5 : 20, true);
        DistanceInput = new NumericInput("眼前距离（米）", _distance, 1.5, 8);
		bool flag = RuntimeFeatureSupport.ImplementedIds.Contains(descriptor.Id);
		IsAvailable = ((runtime != null && (capabilities?.CanExecute(descriptor.RequiredCapability) ?? false)) & flag) && (descriptor.Control != FeatureControl.Generator || Options.Count > 0);
		_status = ((!flag) ? "当前版本尚未完成安全验证" : ((runtime == null) ? "等待连接" : (((object)capabilities == null || !capabilities.CanExecute(descriptor.RequiredCapability)) ? "当前会话不允许" : ((descriptor.Control == FeatureControl.Generator && Options.Count == 0) ? "等待实时目录" : "准备就绪"))));
		_stateLabel = (IsAvailable ? "待命" : "不可用");
		if (descriptor.Control == FeatureControl.Toggle && (object)toggleState != null)
		{
			_enabled = toggleState.Enabled;
			_status = toggleState.Status;
			_stateLabel = toggleState.StateLabel;
			_isSuccessful = toggleState.IsSuccessful;
            if (IsSpin)
            {
                _spinSpeedText = toggleState.SpinSpeedText;
                _appliedSpinSpeed = toggleState.AppliedSpinSpeed;
            }
		}
		if (IsClientProtectionRestricted && runtime != null)
		{
			IsAvailable = true;
			_enabled = false;
			_isSuccessful = false;
			_status = "由房主端结算；客机点击开关查看说明，本机不会开启。";
			_stateLabel = "需房主支持";
		}
        ApplySpinSpeedCommand = new RelayCommand(() =>
        {
            if (CanApplySpinSpeed) _ = ExecuteToggleAsync(true, true);
        });
        ExecuteCommand = new RelayCommand(() =>
		{
			Execute(_runtime, _capabilities);
		});
		CleanupCommand = new RelayCommand(() =>
		{
			CleanupGeneratedAsync();
		});
	}

	private void SelectFirstFiltered()
	{
		if (_restoringParameters) return;
		if (!FilteredOptions.Contains(SelectedOption))
		{
			SelectedOption = FilteredOptions.FirstOrDefault();
		}
	}

	public FeatureToggleState CaptureToggleState()
	{
		return new FeatureToggleState(Enabled, Status, StateLabel, IsSuccessful)
        {
            SpinSpeedText = SpinSpeedText,
            AppliedSpinSpeed = _appliedSpinSpeed
        };
	}

	public void RefreshSponsorAccess()
	{
		Changed("ExecuteButtonText");
		Changed("RequiresSponsor");
	}

	public void ApplyExternalToggleState(FeatureToggleState state)
	{
		if (IsClientProtectionRestricted)
		{
			state = ClientProtectionNotice.DisabledState;
		}
		_suppressToggleExecution = true;
		try
		{
			_enabled = state.Enabled;
			_status = state.Status;
			_stateLabel = state.StateLabel;
			_isSuccessful = state.IsSuccessful;
            if (IsSpin)
            {
                _spinSpeedText = state.SpinSpeedText;
                _appliedSpinSpeed = state.AppliedSpinSpeed;
                Changed(nameof(SpinSpeedText));
                Changed(nameof(SpinSpeedValidationMessage));
                Changed(nameof(HasSpinSpeedError));
                Changed(nameof(CanApplySpinSpeed));
            }
			Changed("Enabled");
			Changed("Status");
			Changed("StateLabel");
			Changed("IsSuccessful");
			Changed("VisualState");
		}
		finally
		{
			_suppressToggleExecution = false;
		}
		NotifyStateChanged();
	}

	private void NotifyStateChanged()
	{
		if (Descriptor.Control == FeatureControl.Toggle)
		{
			_stateChanged?.Invoke(this);
		}
	}

	private async Task ExecuteToggleAsync(bool desired, bool? previousState = null)
	{
        if (desired && _executionBlockReason?.Invoke() is string reason) { RevertToggle(false); Status=reason; StateLabel="待开启"; NotifyStateChanged(); return; }
		if (ShowClientProtectionNotice())
		{
			return;
		}
		bool previous = previousState ?? !desired;
        double spinSpeed = 4d;
        if (IsSpin && desired && !TrySpinSpeed(SpinSpeedText, out spinSpeed))
        {
            Status = "请输入每秒 2–15 圈的转速";
            RevertToggle(previous);
            return;
        }
		if (!IsAvailable || _runtime == null || (object)_capabilities == null)
		{
			Status = "当前会话不可执行此功能";
			StateLabel = "不可用";
			RevertToggle(previous);
			return;
		}
		IsBusy = true;
		IsSuccessful = false;
		StateLabel = "执行中";
		Status = (desired ? "正在开启并等待运行时确认…" : "正在关闭并恢复原始状态…");
		try
		{
			JsonElement arguments = IsSpin ? SpinArguments(desired, spinSpeed) : JsonSerializer.SerializeToElement(new
			{
				enabled = desired
			});
			OperationReceipt operationReceipt = await new FeatureEngine(_runtime).ExecuteAsync(new FeatureCommand(Descriptor.Id, arguments, Descriptor.RequiredCapability), _capabilities, CancellationToken.None);
			ReceiptStatus status = operationReceipt.Status;
			bool flag = (uint)status <= 1u;
			bool flag2 = flag;
            if (IsSpin && desired && flag2) _appliedSpinSpeed = spinSpeed;
			Status = operationReceipt.Message;
			int num;
			if (desired & flag2)
			{
				JsonElement? applied = operationReceipt.Applied;
				if (applied.HasValue)
				{
					JsonElement valueOrDefault = applied.GetValueOrDefault();
					if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("applied", out var value))
					{
						num = ((value.ValueKind == JsonValueKind.False) ? 1 : 0);
						goto IL_01c1;
					}
				}
			}
			num = 0;
			goto IL_01c1;
			IL_020d:
			object obj;
			string waitingFor = (string)obj;
			FeatureItemViewModel featureItemViewModel = this;
			bool flag3;
			string stateLabel;
			if (flag3)
			{
				stateLabel = WaitingLabel(waitingFor);
			}
			else if (flag2)
			{
				stateLabel = (desired ? "已开启" : "已关闭");
			}
			else
			{
				stateLabel = "执行失败";
			}
			featureItemViewModel.StateLabel = stateLabel;
			if (((flag2 && !flag3) & desired) && !Descriptor.Advanced)
			{
				_playEnabledSound?.Invoke();
			}
			if (!flag2)
			{
				RevertToggle(previous);
			}
			return;
			IL_01c1:
			flag3 = (byte)num != 0;
			IsSuccessful = flag2 && !flag3;
			if (flag3)
			{
				JsonElement? applied = operationReceipt.Applied;
				if (applied.HasValue && applied.GetValueOrDefault().TryGetProperty("waitingFor", out var value2))
				{
					obj = value2.GetString();
					goto IL_020d;
				}
			}
			obj = null;
			goto IL_020d;
		}
		catch (Exception ex)
		{
			Status = "执行失败 · " + ex.Message;
			StateLabel = "执行失败";
			IsSuccessful = false;
			RevertToggle(previous);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private static string WaitingLabel(string? waitingFor)
	{
		return waitingFor switch
		{
			"Weapon" => "等待枪械", 
			"Melee" => "等待近战", 
			"FishingRod" => "等待钓竿", 
			"Bait" => "等待鱼饵", 
			"HookedFish" => "等待上鱼", 
			_ => "等待条件", 
		};
	}

	private void RevertToggle(bool value)
	{
		_suppressToggleExecution = true;
		try
		{
			Enabled = value;
		}
		finally
		{
			_suppressToggleExecution = false;
		}
	}

	private async Task Execute(IGameRuntimeClient? runtime, CapabilitySet? capabilities)
	{
		if (IsBusy || ShowClientProtectionNotice())
		{
			return;
		}
        if (Descriptor.Control == FeatureControl.Generator && HasUnresolvedCatalogChoice)
        {
            Status = "上次选择的物品或配件当前不可用，请重新选择后生成";
            StateLabel = "待选择"; IsSuccessful = false; return;
        }
        string inputError = Descriptor.Control switch
        {
            FeatureControl.Number => NumericInput.FirstError(ValueInput),
            FeatureControl.Generator => NumericInput.FirstError(ValueInput, QuantityInput, DistanceInput,
                Descriptor.Id == "generation.fish" ? WeightInput : QuantityInput),
            FeatureControl.Coordinates => NumericInput.FirstError(XInput, YInput, ZInput),
            _ => string.Empty
        };
        if (inputError.Length != 0)
        {
            Status = inputError; StateLabel = "输入无效"; IsSuccessful = false;
            return;
        }
		if (!IsAvailable || runtime == null || (object)capabilities == null)
		{
			Status = "游戏连接并验证后可执行";
			StateLabel = "不可用";
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize(Descriptor.Id))
		{
			Status = "此操作需要激活赞助版；未向游戏发送命令。";
			StateLabel = "需要激活";
			IsSuccessful = false;
			return;
		}
		IsBusy = true;
		IsSuccessful = false;
		StateLabel = "执行中";
		Status = "正在发送并等待游戏运行时返回…";
        var invokedParameters=CaptureParameters();
		try
		{
			JsonElement arguments = JsonSerializer.SerializeToElement(Descriptor.Control switch
			{
                FeatureControl.Toggle when IsSpin => SpinArguments(Enabled, Enabled ? RequireSpinSpeed() : 4d),
				FeatureControl.Toggle => (object)new
				{
					enabled = Enabled
				}, 
				FeatureControl.Number => new
				{
					value = Value
				}, 
				FeatureControl.Generator => new
				{
					value = (int)Value,
					kind = SelectedOption?.Kind,
					quantity = Quantity,
					distance = Distance,
					dead = (Descriptor.Id == "generation.fish" && DeadFish),
					rare = Rare,
					weightMultiplier = WeightMultiplier,
					skin = SelectedSkin?.Id,
					attachments = new
					{
						sight = SelectedSight?.Id,
						barrel = SelectedBarrel?.Id,
						ammo = SelectedAmmo?.Id,
						extendedMag = ExtendedMagazine,
						laser = LaserSight
					}
				}, 
				FeatureControl.Coordinates => new
				{
					x = X,
					y = Y,
					z = Z
				}, 
				_ => new
				{
					value = Value
				}, 
			});
			OperationReceipt operationReceipt = await new FeatureEngine(runtime).ExecuteAsync(new FeatureCommand(Descriptor.Id, arguments, Descriptor.RequiredCapability, SaveAfter && CanRequestNativeSave), capabilities, CancellationToken.None);
			Status = operationReceipt.Message;
			IsSuccessful = operationReceipt.Status == ReceiptStatus.Succeeded;
			if (IsSuccessful && Descriptor.Control == FeatureControl.Generator) { _generated?.Invoke(this,invokedParameters); NotifyCatalogExperience(); }
			StateLabel = operationReceipt.Status switch
			{
				ReceiptStatus.Succeeded => "已完成", 
				ReceiptStatus.AppliedUnverified => (Descriptor.Control != FeatureControl.Generator) ? "已调用" : "未确认生成", 
				ReceiptStatus.RolledBack => "已回滚", 
				_ => "执行失败", 
			};
		}
		catch (Exception ex)
		{
			Status = "执行失败 · " + ex.Message;
			StateLabel = "执行失败";
			IsSuccessful = false;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private bool ShowClientProtectionNotice()
	{
		if (!IsClientProtectionRestricted || _runtime == null)
		{
			return false;
		}
		ApplyExternalToggleState(ClientProtectionNotice.DisabledState);
		_showFeatureNotice?.Invoke("客机功能说明", "当前处于好友房间（客机）。此功能由房主端结算，仅开启本机修改器无法保证生效。请由房主在“房间成员”中为你配置相应保护。本次未开启此功能。");
		return true;
	}

	private async Task CleanupGeneratedAsync()
	{
		if (_runtime != null)
		{
			CapabilitySet? capabilities = _capabilities;
			if ((object)capabilities != null && capabilities.CanExecute(Capability.HostWorld))
			{
				if (!_sponsorAccessGate.TryAuthorize("generation.cleanup"))
				{
					Status = "此操作需要激活赞助版；未向游戏发送命令。";
					StateLabel = "需要激活";
					IsSuccessful = false;
					return;
				}
				IsBusy = true;
				StateLabel = "清理中";
				try
				{
					RuntimeCommandResult runtimeCommandResult = await _runtime.ExecuteAsync(new FeatureCommand("generation.cleanup", JsonSerializer.SerializeToElement(new { }), Capability.HostWorld), CancellationToken.None);
					Status = runtimeCommandResult.Message;
					StateLabel = (runtimeCommandResult.Succeeded ? "已清理" : "清理失败");
					IsSuccessful = runtimeCommandResult.Succeeded;
					return;
				}
				catch (Exception ex)
				{
					Status = "清理失败 · " + ex.Message;
					StateLabel = "清理失败";
					IsSuccessful = false;
					return;
				}
				finally
				{
					IsBusy = false;
				}
			}
		}
		Status = "当前会话不允许清理生成实体";
	}

	private void Changed([CallerMemberName] string? name = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
