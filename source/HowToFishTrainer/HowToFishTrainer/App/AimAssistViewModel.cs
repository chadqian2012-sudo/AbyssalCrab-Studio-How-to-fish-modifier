using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed class AimAssistViewModel : INotifyPropertyChanged
{
	private IGameRuntimeClient? _runtime;

	private CapabilitySet _capabilities = CapabilitySet.Empty;

	private bool _enabled;

	private AimAssistTriggerOption _selectedTrigger;

	private bool _busy;

	private string _status = "等待连接游戏原生辅助瞄准";

	private int _connectionEpoch;
    private bool _applyAgain;
    private bool _lastAppliedEnabled;
    private bool _leadEnabled = true;
    private bool _headLockEnabled;

    public bool LeadEnabled
    {
        get => _leadEnabled;
        set { if (Set(ref _leadEnabled, value) && _runtime != null) _ = ApplyAsync(); }
    }

    public bool HeadLockEnabled
    {
        get => _headLockEnabled;
        set
        {
            if (!Set(ref _headLockEnabled, value) || _runtime == null) return;
            // Restoration happens before Connect; restoring this preference never enables a lock.
            if (value && !Enabled) Enabled = true;
            else _ = ApplyAsync();
        }
    }

    public NumericInput StrengthInput { get; } = new("辅助强度", 60, 0, 100, true);
    public NumericInput AngleInput { get; } = new("静默瞄准角度", 8, 2, 18);
    public NumericInput DistanceInput { get; } = new("最大距离", 35, 10, 80);

	public ObservableCollection<AimAssistTriggerOption> TriggerModes { get; }

	public AimAssistTriggerOption SelectedTrigger
	{
		get
		{
			return _selectedTrigger;
		}
		set
		{
            Set(ref _selectedTrigger, value?.Value == "ads" ? value : TriggerModes[0], "SelectedTrigger");
		}
	}

	public bool Enabled
	{
		get
		{
			return _enabled;
		}
		set
		{
			if (Set(ref _enabled, value, "Enabled") && _runtime != null) _ = ApplyAsync();
		}
	}

	public int Strength
	{
		get
		{
			return (int)StrengthInput.Value;
		}
		set
		{
			StrengthInput.Value = value;
			OnPropertyChanged(nameof(Strength));
		}
	}

	public double AcquireAngle
	{
		get
		{
			return AngleInput.Value;
		}
		set
		{
			AngleInput.Value = value;
			OnPropertyChanged(nameof(AcquireAngle));
		}
	}

	public double MaximumDistance
	{
		get
		{
			return DistanceInput.Value;
		}
		set
		{
			DistanceInput.Value = value;
			OnPropertyChanged(nameof(MaximumDistance));
		}
	}

	public bool Busy
	{
		get
		{
			return _busy;
		}
		private set
		{
			if (Set(ref _busy, value, "Busy"))
			{
				OnPropertyChanged("CanConfigure");
			}
		}
	}

	public bool CanConfigure
	{
		get
		{
			if (_runtime != null && _capabilities.CanExecute(Capability.OwnedPlayer))
			{
				return !Busy;
			}
			return false;
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		private set
		{
			Set(ref _status, value, "Status");
		}
	}

	public ICommand ApplyCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public AimAssistViewModel()
	{
		int num = 1;
		List<AimAssistTriggerOption> list = new List<AimAssistTriggerOption>(num);
		CollectionsMarshal.SetCount(list, num);
		Span<AimAssistTriggerOption> span = CollectionsMarshal.AsSpan(list);
		int num2 = 0;
		span[num2] = new AimAssistTriggerOption("ads", "开镜自动锁定", "开镜时选择范围内实际距离最近的可见目标，松镜解除");
		TriggerModes = new ObservableCollection<AimAssistTriggerOption>(list);
		_selectedTrigger = TriggerModes[0];
		ApplyCommand = new RelayCommand(() =>
		{
			ApplyAsync();
		});
	}

	public void Connect(IGameRuntimeClient runtime, CapabilitySet capabilities)
	{
		Interlocked.Increment(ref _connectionEpoch);
		_runtime = runtime;
		_capabilities = capabilities;
        // A session transition clears runtime locks. Start the new generation idle/off;
        // old completions must neither keep it busy nor replay old enable intent into it.
        _applyAgain = false;
        _lastAppliedEnabled = false;
        Set(ref _enabled, false, nameof(Enabled));
        Busy = false;
		Status = (capabilities.CanExecute(Capability.OwnedPlayer) ? "已连接 · 辅助瞄准待开启" : "当前会话无法修改本机玩家");
		OnPropertyChanged("CanConfigure");
	}

	public void Disconnect()
	{
		Interlocked.Increment(ref _connectionEpoch);
		_runtime = null;
		_capabilities = CapabilitySet.Empty;
        _applyAgain = false;
        _lastAppliedEnabled = false;
        Set(ref _enabled, false, nameof(Enabled));
		Busy = false;
		Status = "运行时已断开";
		OnPropertyChanged("CanConfigure");
	}

	private async Task ApplyAsync()
	{
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		if (runtime == null || !_capabilities.CanExecute(Capability.OwnedPlayer))
		{
			return;
		}
        if (Busy) { _applyAgain = true; return; }
        bool desiredEnabled = Enabled;
		Busy = true;
		try
		{
            string error = NumericInput.FirstError(StrengthInput, AngleInput, DistanceInput);
            if (error.Length != 0 && (desiredEnabled || !_lastAppliedEnabled))
            {
                Status = error;
                if (Enabled == desiredEnabled) Set(ref _enabled, _lastAppliedEnabled, nameof(Enabled));
                return;
            }
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(new FeatureCommand("combat.aim_assist", JsonSerializer.SerializeToElement(new
			{
				enabled = desiredEnabled,
				strength = desiredEnabled ? Strength : (int)StrengthInput.LastValidValue,
				angle = desiredEnabled ? AcquireAngle : AngleInput.LastValidValue,
				distance = desiredEnabled ? MaximumDistance : DistanceInput.LastValidValue,
				trigger = SelectedTrigger.Value,
                leadEnabled = LeadEnabled,
                headLockEnabled = HeadLockEnabled
			}), Capability.OwnedPlayer), CancellationToken.None);
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
                if (runtimeCommandResult.Succeeded) _lastAppliedEnabled = desiredEnabled;
                else if (Enabled == desiredEnabled) Set(ref _enabled, _lastAppliedEnabled, nameof(Enabled));
				Status = (runtimeCommandResult.Succeeded ? runtimeCommandResult.Message : ("辅助瞄准应用失败 · " + runtimeCommandResult.Message));
			}
		}
		catch (Exception ex)
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
                if (Enabled == desiredEnabled) Set(ref _enabled, _lastAppliedEnabled, nameof(Enabled));
				Status = "辅助瞄准应用失败 · " + ex.Message;
			}
		}
		finally
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				Busy = false;
                if (_applyAgain) { _applyAgain = false; _ = ApplyAsync(); }
			}
		}
	}

	private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		return true;
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
