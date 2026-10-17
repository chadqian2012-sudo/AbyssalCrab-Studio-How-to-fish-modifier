using System;
using System.Collections.Generic;
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

public sealed class SlotMachineControlViewModel : INotifyPropertyChanged
{
	private readonly ISponsorAccessGate _sponsorAccessGate;

	private IGameRuntimeClient? _runtime;

	private CapabilitySet _capabilities = CapabilitySet.Empty;

	private int _targetRarity = 3;

	private bool _enabled;

	private bool _busy;

	private string _status = "等待房主运行时";

	private int _connectionEpoch;

	public IReadOnlyList<SlotMachineRarityOption> RarityOptions { get; }

	public SlotMachineRarityOption SelectedRarity
	{
		get
		{
			return RarityOptions.First((SlotMachineRarityOption option) => option.Value == _targetRarity);
		}
		set
		{
			if ((object)value != null && Set(ref _targetRarity, value.Value, "SelectedRarity"))
			{
				OnPropertyChanged("TargetRarityLabel");
			}
		}
	}

	public int TargetRarity
	{
		get
		{
			return _targetRarity;
		}
		set
		{
			if (Set(ref _targetRarity, Math.Clamp(value, 1, 3), "TargetRarity"))
			{
				OnPropertyChanged("SelectedRarity");
			}
		}
	}

	public string TargetRarityLabel => "目标档位 · " + SelectedRarity.Label;

	public bool Enabled
	{
		get
		{
			return _enabled;
		}
		private set
		{
			Set(ref _enabled, value, "Enabled");
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

	public bool CanConfigure
	{
		get
		{
			if (_runtime != null && _capabilities.CanExecute(Capability.HostWorld))
			{
				return !_busy;
			}
			return false;
		}
	}

	public bool CanDisable
	{
		get
		{
			if (CanConfigure)
			{
				return Enabled;
			}
			return false;
		}
	}

	public ICommand ApplyCommand { get; }

	public ICommand DisableCommand { get; }

	public ICommand RefreshCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public SlotMachineControlViewModel(ISponsorAccessGate sponsorAccessGate)
	{
		_sponsorAccessGate = sponsorAccessGate;
		RarityOptions = new _003C_003Ez__ReadOnlyArray<SlotMachineRarityOption>(new SlotMachineRarityOption[3]
		{
			new SlotMachineRarityOption(1, "普通", "优先普通档，缺少时自动降级到可用档位"),
			new SlotMachineRarityOption(2, "稀有", "优先稀有档，缺少时选择最接近的有效档位"),
			new SlotMachineRarityOption(3, "传说", "优先传说档，缺少时选择最接近的有效档位")
		});
		ApplyCommand = new RelayCommand(() =>
		{
			ApplyAsync();
		});
		DisableCommand = new RelayCommand(() =>
		{
			DisableAsync();
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
		Enabled = false;
		Status = (capabilities.CanExecute(Capability.HostWorld) ? "房主运行时已就绪 · 尚未开启" : "仅房主或单人世界可用");
		NotifyCommands();
		RefreshAsync();
	}

	public void Disconnect()
	{
		Interlocked.Increment(ref _connectionEpoch);
		_runtime = null;
		_capabilities = CapabilitySet.Empty;
		_busy = false;
		Enabled = false;
		Status = "等待房主运行时";
		NotifyCommands();
	}

	public async Task RefreshAsync()
	{
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		if (runtime == null || !_capabilities.CanExecute(Capability.HostWorld))
		{
			Status = "仅房主或单人世界可用";
			NotifyCommands();
			return;
		}
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(Command("features.proofs", new { }), CancellationToken.None);
			if (epoch != Volatile.Read(in _connectionEpoch) || runtime != _runtime)
			{
				return;
			}
			JsonElement? applied = runtimeCommandResult.Applied;
			if (!applied.HasValue)
			{
				return;
			}
			JsonElement valueOrDefault = applied.GetValueOrDefault();
			JsonElement value = valueOrDefault;
			if ((valueOrDefault.ValueKind == JsonValueKind.Object && !valueOrDefault.TryGetProperty("features", out value)) || value.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			JsonElement? jsonElement = value.EnumerateArray().FirstOrDefault((JsonElement item) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("featureId", out var value4) && value4.ValueKind == JsonValueKind.String && value4.GetString() == "fishing.skin_jackpot");
			if (jsonElement.HasValue)
			{
				JsonElement valueOrDefault2 = jsonElement.GetValueOrDefault();
				if (valueOrDefault2.TryGetProperty("state", out var value2))
				{
					string text = value2.GetString();
					bool enabled = ((text == "waiting" || text == "applied") ? true : false);
					Enabled = enabled;
					string text2 = ((valueOrDefault2.TryGetProperty("reason", out var value3) && value3.ValueKind == JsonValueKind.String) ? value3.GetString() : null);
					SlotMachineControlViewModel slotMachineControlViewModel = this;
					string status;
					if (string.IsNullOrWhiteSpace(text2))
					{
						status = (Enabled ? ("已开启 · " + TargetRarityLabel + " · 优先未拥有皮肤") : "尚未开启");
					}
					else
					{
						status = text2;
					}
					slotMachineControlViewModel.Status = status;
				}
			}
		}
		catch (Exception ex)
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				Status = "读取状态失败 · " + ex.Message;
			}
		}
	}

	private async Task ApplyAsync()
	{
		if (!CanConfigure)
		{
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("fishing.skin_jackpot"))
		{
			Status = "老虎机功能需要激活赞助版；未向游戏发送命令。";
			return;
		}
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		_busy = true;
		NotifyCommands();
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(Command("fun.slot_machine.rarity", new
			{
				value = TargetRarity
			}), CancellationToken.None);
			if (!runtimeCommandResult.Succeeded)
			{
				Status = runtimeCommandResult.Message;
				return;
			}
			RuntimeCommandResult runtimeCommandResult2 = await runtime.ExecuteAsync(Command("fishing.skin_jackpot", new
			{
				enabled = true
			}), CancellationToken.None);
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				Enabled = runtimeCommandResult2.Succeeded;
				Status = (runtimeCommandResult2.Succeeded ? ("已开启 · " + TargetRarityLabel + " · 优先未拥有皮肤；奖池全收集后恢复原版，可能重复") : runtimeCommandResult2.Message);
			}
		}
		catch (Exception ex)
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				Status = "应用失败 · " + ex.Message;
			}
		}
		finally
		{
			_busy = false;
			NotifyCommands();
		}
	}

	private async Task DisableAsync()
	{
		if (!CanConfigure || !Enabled || !_sponsorAccessGate.TryAuthorize("fishing.skin_jackpot"))
		{
			return;
		}
		IGameRuntimeClient runtime = _runtime;
		_busy = true;
		NotifyCommands();
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(Command("fishing.skin_jackpot", new
			{
				enabled = false
			}), CancellationToken.None);
			Enabled = false;
			Status = (runtimeCommandResult.Succeeded ? "已关闭 · 恢复原生老虎机结果" : runtimeCommandResult.Message);
		}
		catch (Exception ex)
		{
			Status = "关闭失败 · " + ex.Message;
		}
		finally
		{
			_busy = false;
			NotifyCommands();
		}
	}

	private static FeatureCommand Command(string id, object args)
	{
		return new FeatureCommand(id, JsonSerializer.SerializeToElement(args), Capability.HostWorld);
	}

	private void NotifyCommands()
	{
		OnPropertyChanged("CanConfigure");
		OnPropertyChanged("CanDisable");
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
