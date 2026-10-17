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

public sealed class FunEventsViewModel : INotifyPropertyChanged
{
	private readonly ISponsorAccessGate _sponsorAccessGate;

	private IGameRuntimeClient? _runtime;

	private CapabilitySet _capabilities = CapabilitySet.Empty;

	private bool _busy;

	private string _hostSyncStatus = "等待房主运行时";

	private string _currentWaveStatus = "尚未触发趣味事件";

	private int _currentWaveCount;

	private int _totalEntityCount;

	private int _connectionEpoch;
    private bool _lastWaveSucceeded;
    private int _sessionSpawnedCount;
    public int SessionSpawnedCount { get => _sessionSpawnedCount; private set => Set(ref _sessionSpawnedCount,value); }
    public event Action? CleanupRequested;
    public async Task<bool> ExecuteWaveAsync(string id)
    {
        var card=Events.FirstOrDefault(e=>e.Id==id);
        if(card?.CanExecute!=true || !CanExecuteWave)return false;
        await ExecuteAsync(card);return _lastWaveSucceeded;
    }

	public ObservableCollection<FunEventCardViewModel> Events { get; }

	public RouletteControlViewModel Roulette { get; }

	public SlotMachineControlViewModel SlotMachine { get; }

	public string HostSyncStatus
	{
		get
		{
			return _hostSyncStatus;
		}
		private set
		{
			Set(ref _hostSyncStatus, value, "HostSyncStatus");
		}
	}

	public string CurrentWaveStatus
	{
		get
		{
			return _currentWaveStatus;
		}
		private set
		{
			Set(ref _currentWaveStatus, value, "CurrentWaveStatus");
		}
	}

	public int CurrentWaveCount
	{
		get
		{
			return _currentWaveCount;
		}
		private set
		{
			Set(ref _currentWaveCount, value, "CurrentWaveCount");
		}
	}

	public int TotalEntityCount
	{
		get
		{
			return _totalEntityCount;
		}
		private set
		{
			Set(ref _totalEntityCount, value, "TotalEntityCount");
		}
	}

	public bool CanExecuteWave
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

	public bool CanCleanup => CanExecuteWave;

	public ICommand CleanupAllCommand { get; }

	public ICommand RefreshCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public FunEventsViewModel(ISponsorAccessGate? sponsorAccessGate = null)
	{
		_sponsorAccessGate = sponsorAccessGate ?? AllowAllSponsorAccessGate.Instance;
		Roulette = new RouletteControlViewModel(_sponsorAccessGate);
		SlotMachine = new SlotMachineControlViewModel(_sponsorAccessGate);
		Events = new ObservableCollection<FunEventCardViewModel>(FunEventDefinitions.All.Select((FunEventDefinition item) => new FunEventCardViewModel(item, ExecuteAsync)));
		CleanupAllCommand = new RelayCommand(() =>
		{
            CleanupRequested?.Invoke();
			CleanupAsync();
		});
		RefreshCommand = new RelayCommand(() =>
		{
			RefreshAvailabilityAsync();
		});
	}

	public FunEventsViewModel(IGameRuntimeClient runtime, CapabilitySet capabilities, ISponsorAccessGate? sponsorAccessGate = null)
		: this(sponsorAccessGate)
	{
		Connect(runtime, capabilities);
	}

	public void Connect(IGameRuntimeClient runtime, CapabilitySet capabilities)
	{
        _busy=false; SessionSpawnedCount=0;
		Interlocked.Increment(ref _connectionEpoch);
		_runtime = runtime;
		_capabilities = capabilities;
		Roulette.Connect(runtime, capabilities);
		SlotMachine.Connect(runtime, capabilities);
		int currentWaveCount = (TotalEntityCount = 0);
		CurrentWaveCount = currentWaveCount;
		HostSyncStatus = (capabilities.CanExecute(Capability.HostWorld) ? "房主同步已就绪" : "仅房主或单人世界可触发");
		UpdateCardsForAuthority();
		RefreshAvailabilityAsync();
	}

	public void Disconnect()
	{
		Interlocked.Increment(ref _connectionEpoch);
		_runtime = null;
		_capabilities = CapabilitySet.Empty;
		Roulette.Disconnect();
		SlotMachine.Disconnect();
		_busy = false;
		HostSyncStatus = "等待房主运行时";
		CurrentWaveStatus = "运行时已断开";
		int currentWaveCount = (TotalEntityCount = 0);
		CurrentWaveCount = currentWaveCount;
		UpdateCardsForAuthority();
	}

	public Task RefreshAvailabilityAsync()
	{
		return Task.WhenAll(RefreshAvailabilityAsync(Volatile.Read(in _connectionEpoch)), Roulette.RefreshAsync(), SlotMachine.RefreshAsync());
	}

	private async Task RefreshAvailabilityAsync(int epoch)
	{
		IGameRuntimeClient runtime = _runtime;
		if (runtime == null || !_capabilities.CanExecute(Capability.HostWorld))
		{
			UpdateCardsForAuthority();
			return;
		}
		try
		{
			RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(Command("fun.event.catalog", new { }), CancellationToken.None);
			if (epoch != Volatile.Read(in _connectionEpoch) || runtime != _runtime)
			{
				return;
			}
			if (runtimeCommandResult.Succeeded)
			{
				JsonElement? applied = runtimeCommandResult.Applied;
				if (applied.HasValue)
				{
					JsonElement valueOrDefault = applied.GetValueOrDefault();
					JsonElement jsonElement = ((valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("events", out var value)) ? value : valueOrDefault);
					if (jsonElement.ValueKind != JsonValueKind.Array)
					{
						return;
					}
					foreach (JsonElement item in jsonElement.EnumerateArray())
					{
						string id = Text(item, "id");
						FunEventCardViewModel funEventCardViewModel = Events.FirstOrDefault((FunEventCardViewModel funEventCardViewModel2) => funEventCardViewModel2.Id == id);
						if (funEventCardViewModel != null)
						{
							funEventCardViewModel.Available = item.TryGetProperty("available", out var value2) && value2.GetBoolean();
							funEventCardViewModel.Reason = Text(item, "reason") ?? (funEventCardViewModel.Available ? "资源已就绪" : "当前不可用");
						}
					}
					if (valueOrDefault.ValueKind == JsonValueKind.Object)
					{
						TotalEntityCount = Int(valueOrDefault, "totalFunEntities", TotalEntityCount);
						CurrentWaveCount = Int(valueOrDefault, "currentWaveCount", CurrentWaveCount);
						if (valueOrDefault.TryGetProperty("busy", out var value3) && value3.ValueKind == JsonValueKind.True)
						{
							CurrentWaveStatus = "上一波仍在生成，请稍候";
						}
						else if (TotalEntityCount == 0)
						{
							CurrentWaveCount = 0;
						}
					}
					HostSyncStatus = "房主目录已同步";
					return;
				}
			}
			CurrentWaveStatus = runtimeCommandResult.Message;
		}
		catch (Exception ex)
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				CurrentWaveStatus = "读取趣味资源失败 · " + ex.Message;
			}
		}
	}

	private async Task ExecuteAsync(FunEventCardViewModel card)
	{
        _lastWaveSucceeded=false;
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		if (runtime == null || !CanExecuteWave)
		{
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("fun.event.execute"))
		{
			CurrentWaveStatus = "此操作需要激活赞助版；未向游戏发送命令。";
			return;
		}
		SetBusy(busy: true, card);
		CurrentWaveStatus = $"正在生成：{card.Title} 0/{card.RequestedCount}";
		try
		{
			try
			{
				RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(Command("fun.event.execute", new
				{
					eventId = card.Id
				}), CancellationToken.None);
				if (epoch != Volatile.Read(in _connectionEpoch) || runtime != _runtime)
				{
					goto end_IL_00e2;
				}
				CurrentWaveStatus = runtimeCommandResult.Message;
                _lastWaveSucceeded=runtimeCommandResult.Succeeded;
				JsonElement? applied = runtimeCommandResult.Applied;
				if (applied.HasValue)
				{
					JsonElement valueOrDefault = applied.GetValueOrDefault();
					ApplyStatus(valueOrDefault);
                    if(_lastWaveSucceeded)SessionSpawnedCount+=Int(valueOrDefault,"registered",0);
				}
				goto end_IL_00dc;
				end_IL_00e2:;
			}
			catch (Exception ex)
			{
				if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
				{
					CurrentWaveStatus = "执行失败 · " + ex.Message;
				}
				goto end_IL_00dc;
			}
			end_IL_00dc:;
		}
		finally
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				SetBusy(busy: false, card);
				await RefreshAvailabilityAsync(epoch);
			}
		}
	}

	private async Task CleanupAsync()
	{
		IGameRuntimeClient runtime = _runtime;
		int epoch = Volatile.Read(in _connectionEpoch);
		if (runtime == null || !_capabilities.CanExecute(Capability.HostWorld) || _busy)
		{
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("fun.event.cleanup"))
		{
			CurrentWaveStatus = "此操作需要激活赞助版；未向游戏发送命令。";
			return;
		}
		SetBusy(busy: true, null);
		try
		{
			try
			{
				RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(Command("fun.event.cleanup", new
				{
					scope = "all"
				}), CancellationToken.None);
				if (epoch != Volatile.Read(in _connectionEpoch) || runtime != _runtime)
				{
					goto end_IL_0097;
				}
				JsonElement? applied = runtimeCommandResult.Applied;
				if (applied.HasValue)
				{
					JsonElement valueOrDefault = applied.GetValueOrDefault();
					ApplyStatus(valueOrDefault);
				}
				CurrentWaveStatus = ((TotalEntityCount > 0) ? $"清理未完成 · 剩余 {TotalEntityCount} 个趣味实体 · {runtimeCommandResult.Message}" : runtimeCommandResult.Message);
				goto end_IL_0091;
				end_IL_0097:;
			}
			catch (Exception ex)
			{
				if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
				{
					CurrentWaveStatus = "清理失败 · " + ex.Message;
				}
				goto end_IL_0091;
			}
			end_IL_0091:;
		}
		finally
		{
			if (epoch == Volatile.Read(in _connectionEpoch) && runtime == _runtime)
			{
				SetBusy(busy: false, null);
				await RefreshAvailabilityAsync(epoch);
			}
		}
	}

	private void ApplyStatus(JsonElement value)
	{
		if (value.ValueKind == JsonValueKind.Object)
		{
			CurrentWaveCount = Int(value, "registered", Int(value, "currentWaveCount", 0));
			TotalEntityCount = Int(value, "totalFunEntities", TotalEntityCount);
		}
	}

	private void SetBusy(bool busy, FunEventCardViewModel? active)
	{
		_busy = busy;
		foreach (FunEventCardViewModel @event in Events)
		{
			@event.Busy = (busy && @event != active) || ((@event == active) & busy);
		}
		OnPropertyChanged("CanExecuteWave");
		OnPropertyChanged("CanCleanup");
	}

	private void UpdateCardsForAuthority()
	{
		bool flag = _runtime != null && _capabilities.CanExecute(Capability.HostWorld);
		foreach (FunEventCardViewModel @event in Events)
		{
			@event.Available = false;
			@event.Reason = (flag ? "等待资源预检" : "仅房主或单人世界可用");
		}
		OnPropertyChanged("CanExecuteWave");
		OnPropertyChanged("CanCleanup");
	}

	private static FeatureCommand Command(string id, object args)
	{
		return new FeatureCommand(id, JsonSerializer.SerializeToElement(args), Capability.HostWorld);
	}

	private static string? Text(JsonElement value, string name)
	{
		if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var value2) || value2.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value2.GetString();
	}

	private static int Int(JsonElement value, string name, int fallback)
	{
		if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var value2) || !value2.TryGetInt32(out var value3))
		{
			return fallback;
		}
		return value3;
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
