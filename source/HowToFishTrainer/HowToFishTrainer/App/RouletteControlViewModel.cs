using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HowToFishTrainer.App.Licensing;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed class RouletteControlViewModel : INotifyPropertyChanged
{
	private readonly ISponsorAccessGate _sponsorAccessGate;

	private IGameRuntimeClient? _runtime;

	private CapabilitySet _capabilities = CapabilitySet.Empty;

	private RouletteOutcomeOption _selectedOutcomeMode;

	private RouletteRoundOption _selectedRoundMode;

	private bool _useOriginalMultiplier = true;

	private bool _busy;

	private string _currentBetLabel = "尚未下注";

	private string _currentWorthLabel = "0";

	private string _multiplierStatusLabel = "原版赔率";

	private string _remainingRoundsLabel = "未启用";

	private string _lastSettlementLabel = "暂无已验证结算";

	private string _status = "等待连接轮盘运行时";

	private int _connectionEpoch;

	public ObservableCollection<RouletteOutcomeOption> OutcomeModes { get; }

	public ObservableCollection<RouletteRoundOption> RoundModes { get; }

	public RouletteOutcomeOption SelectedOutcomeMode
	{
		get
		{
			return _selectedOutcomeMode;
		}
		set
		{
			Set(ref _selectedOutcomeMode, value, "SelectedOutcomeMode");
		}
	}

	public RouletteRoundOption SelectedRoundMode
	{
		get
		{
			return _selectedRoundMode;
		}
		set
		{
			Set(ref _selectedRoundMode, value, "SelectedRoundMode");
		}
	}

	public bool UseOriginalMultiplier
	{
		get
		{
			return _useOriginalMultiplier;
		}
		set
		{
			if (Set(ref _useOriginalMultiplier, value, "UseOriginalMultiplier"))
			{
				OnPropertyChanged("CanEditMultiplier");
			}
		}
	}

	public bool CanEditMultiplier => !UseOriginalMultiplier;

    public NumericInput MultiplierInput { get; } = new("自定义赔率", 2, 1, 100, true);

	public int CustomMultiplier
	{
		get
		{
			return (int)MultiplierInput.Value;
		}
		set
		{
			MultiplierInput.Value = value;
			OnPropertyChanged(nameof(CustomMultiplier));
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
			if (_runtime != null && _capabilities.CanExecute(Capability.HostWorld))
			{
				return !Busy;
			}
			return false;
		}
	}

	public string CurrentBetLabel
	{
		get
		{
			return _currentBetLabel;
		}
		private set
		{
			Set(ref _currentBetLabel, value, "CurrentBetLabel");
		}
	}

	public string CurrentWorthLabel
	{
		get
		{
			return _currentWorthLabel;
		}
		private set
		{
			Set(ref _currentWorthLabel, value, "CurrentWorthLabel");
		}
	}

	public string MultiplierStatusLabel
	{
		get
		{
			return _multiplierStatusLabel;
		}
		private set
		{
			Set(ref _multiplierStatusLabel, value, "MultiplierStatusLabel");
		}
	}

	public string RemainingRoundsLabel
	{
		get
		{
			return _remainingRoundsLabel;
		}
		private set
		{
			Set(ref _remainingRoundsLabel, value, "RemainingRoundsLabel");
		}
	}

	public string LastSettlementLabel
	{
		get
		{
			return _lastSettlementLabel;
		}
		private set
		{
			Set(ref _lastSettlementLabel, value, "LastSettlementLabel");
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

	public ICommand ResetCommand { get; }

	public ICommand RefreshCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public RouletteControlViewModel(ISponsorAccessGate? sponsorAccessGate = null)
	{
		_sponsorAccessGate = sponsorAccessGate ?? AllowAllSponsorAccessGate.Instance;
		int num = 4;
		List<RouletteOutcomeOption> list = new List<RouletteOutcomeOption>(num);
		CollectionsMarshal.SetCount(list, num);
		Span<RouletteOutcomeOption> span = CollectionsMarshal.AsSpan(list);
		int num2 = 0;
		span[num2] = new RouletteOutcomeOption("followbet", "跟随当前下注", "本局结果跟随玩家已下注颜色");
		num2++;
		span[num2] = new RouletteOutcomeOption("black", "指定黑色", "原生结算结果固定为黑色");
		num2++;
		span[num2] = new RouletteOutcomeOption("red", "指定红色", "原生结算结果固定为红色");
		num2++;
		span[num2] = new RouletteOutcomeOption("green", "指定绿色", "原生结算结果固定为绿色");
		OutcomeModes = new ObservableCollection<RouletteOutcomeOption>(list);
		num2 = 4;
		List<RouletteRoundOption> list2 = new List<RouletteRoundOption>(num2);
		CollectionsMarshal.SetCount(list2, num2);
		Span<RouletteRoundOption> span2 = CollectionsMarshal.AsSpan(list2);
		num = 0;
		span2[num] = new RouletteRoundOption(1, "仅下一局");
		num++;
		span2[num] = new RouletteRoundOption(5, "接下来 5 局");
		num++;
		span2[num] = new RouletteRoundOption(10, "接下来 10 局");
		num++;
		span2[num] = new RouletteRoundOption(-1, "持续生效");
		RoundModes = new ObservableCollection<RouletteRoundOption>(list2);
		_selectedOutcomeMode = OutcomeModes[0];
		_selectedRoundMode = RoundModes[0];
		ApplyCommand = new RelayCommand(() =>
		{
			ApplyAsync();
		});
		ResetCommand = new RelayCommand(() =>
		{
			ResetAsync();
		});
		RefreshCommand = new RelayCommand(() =>
		{
			RefreshAsync();
		});
	}

	public void Connect(IGameRuntimeClient runtime, CapabilitySet capabilities)
	{
		Interlocked.Increment(ref _connectionEpoch);
		_runtime = runtime;
		_capabilities = capabilities;
		RouletteControlViewModel rouletteControlViewModel = this;
		string status;
		if (capabilities.CanExecute(Capability.HostWorld))
		{
			status = (_sponsorAccessGate.IsSponsor ? "等待读取原生轮盘状态" : "赞助版专用 · 点击应用可激活");
		}
		else
		{
			status = "好友客机不能控制轮盘；由房主设置后画面与结算将全房同步";
		}
		rouletteControlViewModel.Status = status;
		OnPropertyChanged("CanConfigure");
	}

	public void Disconnect()
	{
		Interlocked.Increment(ref _connectionEpoch);
		_runtime = null;
		_capabilities = CapabilitySet.Empty;
		Busy = false;
		CurrentBetLabel = "尚未下注";
		CurrentWorthLabel = "0";
		RemainingRoundsLabel = "未启用";
		LastSettlementLabel = "暂无已验证结算";
		Status = "运行时已断开";
		OnPropertyChanged("CanConfigure");
	}

	public async Task RefreshAsync()
	{
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		if (runtime != null && _capabilities.CanExecute(Capability.HostWorld))
		{
			if (!_sponsorAccessGate.IsSponsor)
			{
				Status = "赞助版专用 · 点击应用可激活";
			}
			else
			{
				await ExecuteAndReadAsync(runtime, epoch, Command("casino.status", new { }), "读取轮盘状态失败");
			}
		}
	}

	private async Task ApplyAsync()
	{
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		if (runtime == null || !CanConfigure)
		{
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("casino.configure"))
		{
			Status = "此功能需要激活赞助版；未向游戏发送命令。";
			return;
		}
        if (!UseOriginalMultiplier && MultiplierInput.HasError)
        {
            Status = MultiplierInput.Error;
            return;
        }
		bool flag = !UseOriginalMultiplier;
		if (flag)
		{
			int customMultiplier = CustomMultiplier;
			bool flag2 = ((customMultiplier < 1 || customMultiplier > 100) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			Status = "自定义倍率必须在 1–100 倍之间。";
			return;
		}
		int? multiplier = (UseOriginalMultiplier ? ((int?)null) : new int?(CustomMultiplier));
		await ExecuteAndReadAsync(runtime, epoch, Command("casino.configure", new
		{
			outcomeMode = SelectedOutcomeMode.Value,
			multiplier = multiplier,
			rounds = SelectedRoundMode.Rounds
		}), "应用轮盘控制失败");
	}

	private async Task ResetAsync()
	{
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		if (runtime != null && CanConfigure)
		{
			if (!_sponsorAccessGate.TryAuthorize("casino.reset"))
			{
				Status = "此功能需要激活赞助版；未向游戏发送命令。";
			}
			else
			{
				await ExecuteAndReadAsync(runtime, epoch, Command("casino.reset", new { }), "恢复原版轮盘失败");
			}
		}
	}

	private async Task ExecuteAndReadAsync(IGameRuntimeClient runtime, int epoch, FeatureCommand command, string failurePrefix)
	{
		Busy = true;
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(command, CancellationToken.None);
			if (epoch != Volatile.Read(in _connectionEpoch) || runtime != _runtime)
			{
				return;
			}
			JsonElement? jsonElement = runtimeCommandResult.ReadBack ?? runtimeCommandResult.Applied;
			if (runtimeCommandResult.Succeeded && jsonElement.HasValue)
			{
				JsonElement valueOrDefault = jsonElement.GetValueOrDefault();
				if (valueOrDefault.ValueKind == JsonValueKind.Object)
				{
					ApplyStatus(valueOrDefault, runtimeCommandResult.Message);
					return;
				}
			}
			Status = failurePrefix + " · " + runtimeCommandResult.Message;
		}
		catch (Exception ex)
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				Status = failurePrefix + " · " + ex.Message;
			}
		}
		finally
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				Busy = false;
			}
		}
	}

	private void ApplyStatus(JsonElement value, string replyMessage)
	{
		CurrentBetLabel = (Text(value, "betColor") ?? "unknown").ToLowerInvariant() switch
		{
			"black" => "黑色", 
			"red" => "红色", 
			"green" => "绿色", 
			_ => "尚未下注", 
		};
		CurrentWorthLabel = Int(value, "totalWorth", 0).ToString();
		string mode = Text(value, "outcomeMode") ?? "Disabled";
		int? num = NullableInt(value, "multiplier");
		// Runtime status describes the applied configuration; the editors retain the user's pending parameters.
		MultiplierStatusLabel = (num.HasValue ? $"{num.Value} 倍" : "原版赔率");
		int num2 = Int(value, "remainingRounds", 0);
		RemainingRoundsLabel = num2 switch
		{
			-1 => "持续生效", 
			0 => "未启用", 
			_ => $"剩余 {num2} 局", 
		};
		bool? flag = NullableBool(value, "lastWon");
		string value2 = Color(Text(value, "lastOriginalColor"));
		string text = Color(Text(value, "lastFinalColor"));
		bool? flag2 = NullableBool(value, "visualSynced");
		string text2 = Color(Text(value, "lastVisualColor"));
		string lastSettlementLabel;
		if (!flag.HasValue)
		{
			lastSettlementLabel = "暂无已验证结算";
		}
		else
		{
			string text3 = $"上次：{value2} → {text} · {(flag.Value ? "中奖" : "未中")}";
			int? num3 = NullableInt(value, "lastMultiplier");
			string text4;
			if (num3.HasValue)
			{
				int valueOrDefault = num3.GetValueOrDefault();
				text4 = $" · {valueOrDefault} 倍";
			}
			else
			{
				text4 = string.Empty;
			}
			lastSettlementLabel = text3 + text4 + ((flag2 == true) ? (" · 画面" + text2 + " = 结算" + text) : string.Empty);
		}
		LastSettlementLabel = lastSettlementLabel;
		bool flag3 = Bool(value, "available");
		string text5 = Text(value, "message") ?? replyMessage;
		Status = (flag3 ? text5 : ("轮盘暂不可用 · " + text5));
	}

	private static string Color(string? value)
	{
		return (value ?? string.Empty).ToLowerInvariant() switch
		{
			"black" => "黑色", 
			"red" => "红色", 
			"green" => "绿色", 
			_ => "未知", 
		};
	}

	private static FeatureCommand Command(string id, object args)
	{
		return new FeatureCommand(id, JsonSerializer.SerializeToElement(args), Capability.HostWorld);
	}

	private static string? Text(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out var value2) || value2.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value2.GetString();
	}

	private static int Int(JsonElement value, string name, int fallback)
	{
		if (!value.TryGetProperty(name, out var value2) || value2.ValueKind != JsonValueKind.Number || !value2.TryGetInt32(out var value3))
		{
			return fallback;
		}
		return value3;
	}

	private static int? NullableInt(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out var value2) || value2.ValueKind != JsonValueKind.Number || !value2.TryGetInt32(out var value3))
		{
			return null;
		}
		return value3;
	}

	private static bool Bool(JsonElement value, string name)
	{
		if (value.TryGetProperty(name, out var value2))
		{
			return value2.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private static bool? NullableBool(JsonElement value, string name)
	{
		if (value.TryGetProperty(name, out var value2))
		{
			return value2.ValueKind switch
			{
				JsonValueKind.True => (bool?)true, 
				JsonValueKind.False => false, 
				_ => null, 
			};
		}
		return null;
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
