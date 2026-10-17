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

public sealed class RoomMembersViewModel : INotifyPropertyChanged
{
	private IGameRuntimeClient? _runtime;

	private CapabilitySet _capabilities = CapabilitySet.Empty;

	private RoomMemberSnapshot? _selectedMember;

	private string _status = "等待连接房主世界";

	private bool _busy;

	private long _connectionEpoch;

	private string _dropScope = "off";

	private string _rareScope = "off";

	private string _healthScope = "off";

	private string _fullnessScope = "off";

	private string _poisonScope = "off";

	private string _fireScope = "off";

	private string _searchText = string.Empty;

	private string _lastRefreshLabel = "尚未读取";

	private string _lastVerifiedOperation = "暂无已验证操作";

	private string _lastVerifiedAt = "--:--:--";

	private string _policyStateLabel = "尚未设置";

	private readonly Action<RoomMemberSnapshot>? _openInTeleport;

	private readonly ISponsorAccessGate _sponsorAccessGate;

	public ObservableCollection<RoomMemberSnapshot> Members { get; } = new ObservableCollection<RoomMemberSnapshot>();

	public ObservableCollection<string> BatchResults { get; } = new ObservableCollection<string>();

	public IReadOnlyList<RoomMemberSnapshot> FilteredMembers => Members.Where((RoomMemberSnapshot member) => string.IsNullOrWhiteSpace(SearchText) || member.DisplayName.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) || member.OwnerTail.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) || member.RoleLabel.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

	public RoomMemberSnapshot? SelectedMember
	{
		get
		{
			return _selectedMember;
		}
		set
		{
			if (Set(ref _selectedMember, value, "SelectedMember"))
			{
				Changed("HasSelection");
			}
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

	public bool IsBusy
	{
		get
		{
			return _busy;
		}
		private set
		{
			if (Set(ref _busy, value, "IsBusy"))
			{
				Changed("CanManage");
			}
		}
	}

	public bool CanManage
	{
		get
		{
			if (_sponsorAccessGate.IsSponsor && _runtime != null && _capabilities.CanExecute(Capability.HostWorld))
			{
				return !IsBusy;
			}
			return false;
		}
	}

	public bool HasSelection => (object)SelectedMember != null;

	public string DropScope
	{
		get
		{
			return _dropScope;
		}
		set
		{
			Set(ref _dropScope, value, "DropScope");
		}
	}

	public string RareScope
	{
		get
		{
			return _rareScope;
		}
		set
		{
			Set(ref _rareScope, value, "RareScope");
		}
	}

	public string HealthScope
	{
		get
		{
			return _healthScope;
		}
		set
		{
			Set(ref _healthScope, value, "HealthScope");
		}
	}

	public string FullnessScope
	{
		get
		{
			return _fullnessScope;
		}
		set
		{
			Set(ref _fullnessScope, value, "FullnessScope");
		}
	}

	public string PoisonScope
	{
		get
		{
			return _poisonScope;
		}
		set
		{
			Set(ref _poisonScope, value, "PoisonScope");
		}
	}

	public string FireScope
	{
		get
		{
			return _fireScope;
		}
		set
		{
			Set(ref _fireScope, value, "FireScope");
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
			if (Set(ref _searchText, value, "SearchText"))
			{
				Changed("FilteredMembers");
			}
		}
	}

	public int OnlineCount => Members.Count;

	public int AliveCount => Members.Count((RoomMemberSnapshot member) => member.Alive);

	public int DownCount => Members.Count((RoomMemberSnapshot member) => !member.Alive);

	public string LastRefreshLabel
	{
		get
		{
			return _lastRefreshLabel;
		}
		private set
		{
			Set(ref _lastRefreshLabel, value, "LastRefreshLabel");
		}
	}

	public string LastVerifiedOperation
	{
		get
		{
			return _lastVerifiedOperation;
		}
		private set
		{
			Set(ref _lastVerifiedOperation, value, "LastVerifiedOperation");
		}
	}

	public string LastVerifiedAt
	{
		get
		{
			return _lastVerifiedAt;
		}
		private set
		{
			Set(ref _lastVerifiedAt, value, "LastVerifiedAt");
		}
	}

	public string PolicyStateLabel
	{
		get
		{
			return _policyStateLabel;
		}
		private set
		{
			Set(ref _policyStateLabel, value, "PolicyStateLabel");
		}
	}

	public ICommand RefreshCommand { get; }

	public ICommand RestoreHealthCommand { get; }

	public ICommand RestoreFullnessCommand { get; }

	public ICommand ClearStatusCommand { get; }

	public ICommand ClearPoisonCommand { get; }

	public ICommand ClearFireCommand { get; }

	public ICommand RestoreAllCommand { get; }

	public ICommand RestoreRoomHealthCommand { get; }

	public ICommand RestoreRoomFullnessCommand { get; }

	public ICommand ClearRoomStatusCommand { get; }

	public ICommand RestoreRoomAllCommand { get; }

	public ICommand ResetPoliciesCommand { get; }

	public ICommand ReviveCommand { get; }

	public ICommand ApplyPoliciesCommand { get; }

	public ICommand TeleportToHostCommand { get; }

	public ICommand HostToMemberCommand { get; }

	public ICommand OpenSelectedInTeleportCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public RoomMembersViewModel(Action<RoomMemberSnapshot>? openInTeleport = null, ISponsorAccessGate? sponsorAccessGate = null)
	{
		_openInTeleport = openInTeleport;
		_sponsorAccessGate = sponsorAccessGate ?? AllowAllSponsorAccessGate.Instance;
		RefreshCommand = new RelayCommand(() =>
		{
			RefreshAsync();
		});
		RestoreHealthCommand = new RelayCommand(() =>
		{
			RestoreHealthAsync();
		});
		RestoreFullnessCommand = new RelayCommand(() =>
		{
			RestoreFullnessAsync();
		});
		ClearStatusCommand = new RelayCommand(() =>
		{
			ClearStatusAsync();
		});
		ClearPoisonCommand = new RelayCommand(() =>
		{
			ClearPoisonAsync();
		});
		ClearFireCommand = new RelayCommand(() =>
		{
			ClearFireAsync();
		});
		RestoreAllCommand = new RelayCommand(() =>
		{
			RestoreAllAsync();
		});
		RestoreRoomHealthCommand = new RelayCommand(() =>
		{
			RestoreRoomAsync("health");
		});
		RestoreRoomFullnessCommand = new RelayCommand(() =>
		{
			RestoreRoomAsync("fullness");
		});
		ClearRoomStatusCommand = new RelayCommand(() =>
		{
			RestoreRoomAsync("clearStatus");
		});
		RestoreRoomAllCommand = new RelayCommand(() =>
		{
			RestoreRoomAsync("restoreAll");
		});
		ResetPoliciesCommand = new RelayCommand(() =>
		{
			ResetPoliciesAsync();
		});
		ReviveCommand = new RelayCommand(() =>
		{
			ReviveAsync();
		});
		ApplyPoliciesCommand = new RelayCommand(() =>
		{
			ApplyPoliciesAsync();
		});
		TeleportToHostCommand = new RelayCommand(() =>
		{
			TeleportAsync("toHost");
		});
		HostToMemberCommand = new RelayCommand(() =>
		{
			TeleportAsync("hostToPlayer");
		});
		OpenSelectedInTeleportCommand = new RelayCommand(OpenSelectedInTeleport);
	}

	public void Connect(IGameRuntimeClient runtime, CapabilitySet capabilities)
	{
		_connectionEpoch++;
		IsBusy = false;
		_runtime = runtime;
		_capabilities = capabilities;
		RoomMembersViewModel roomMembersViewModel = this;
		string status;
		if (_sponsorAccessGate.IsSponsor)
		{
			status = (capabilities.CanExecute(Capability.HostWorld) ? "房主成员管理已连接，等待读取房间成员" : "好友房当前玩家可使用本机功能；成员远程管理仅房主可用");
		}
		else
		{
			status = "房间成员控制台为赞助版专用功能";
		}
		roomMembersViewModel.Status = status;
		Changed("CanManage");
	}

	public void Disconnect()
	{
		_connectionEpoch++;
		IsBusy = false;
		_runtime = null;
		_capabilities = CapabilitySet.Empty;
		Members.Clear();
		BatchResults.Clear();
		SelectedMember = null;
		LastRefreshLabel = "尚未读取";
		LastVerifiedOperation = "暂无已验证操作";
		LastVerifiedAt = "--:--:--";
		PolicyStateLabel = "尚未设置";
		Status = "等待连接房主世界";
		NotifyCatalogChanged();
		Changed("CanManage");
	}

	public async Task RefreshAsync()
	{
		if (!_sponsorAccessGate.TryAuthorize("players.catalog"))
		{
			Status = "房间成员控制台需要激活赞助版";
			Changed("CanManage");
		}
		else
		{
			if (!CanManage || _runtime == null)
			{
				return;
			}
			string selectedKey = SelectedMember?.Key;
			await Run(async (IGameRuntimeClient runtime) =>
			{
				RuntimeCommandResult runtimeCommandResult = await Send(runtime, "players.catalog", new { });
				if (runtimeCommandResult.Succeeded)
				{
					JsonElement? applied = runtimeCommandResult.Applied;
					if (applied.HasValue && applied.GetValueOrDefault().TryGetProperty("players", out var value))
					{
						RoomMemberSnapshot[] array = value.EnumerateArray().Select(Parse).ToArray();
						int index;
						for (index = Members.Count - 1; index >= 0; index--)
						{
							if (!array.Any((RoomMemberSnapshot snapshot) => snapshot.Key == Members[index].Key))
							{
								Members.RemoveAt(index);
							}
						}
						RoomMemberSnapshot[] array2 = array;
						foreach (RoomMemberSnapshot member in array2)
						{
							int num2 = Members.ToList().FindIndex((RoomMemberSnapshot item) => item.Key == member.Key);
							if (num2 >= 0)
							{
								Members[num2] = member;
							}
							else
							{
								Members.Add(member);
							}
						}
						SelectedMember = Members.FirstOrDefault((RoomMemberSnapshot roomMemberSnapshot) => roomMemberSnapshot.Key == selectedKey) ?? Members.FirstOrDefault();
						LastRefreshLabel = DateTime.Now.ToString("HH:mm:ss");
						Status = $"已读取 {array.Length} 名房间成员 · {LastRefreshLabel}";
						NotifyCatalogChanged();
						return;
					}
				}
				throw new InvalidOperationException(runtimeCommandResult.Message);
			});
		}
	}

	public Task RestoreHealthAsync()
	{
		return MutateVitals("恢复生命", new
		{
			restoreHealth = true
		});
	}

	public Task RestoreFullnessAsync()
	{
		return MutateVitals("恢复饱食", new
		{
			restoreFullness = true
		});
	}

	public Task ClearPoisonAsync()
	{
		return MutateVitals("解除中毒", new
		{
			clearPoison = true
		});
	}

	public Task ClearFireAsync()
	{
		return MutateVitals("扑灭燃烧", new
		{
			clearFire = true
		});
	}

	public Task ClearAllStatusAsync()
	{
		return MutateVitals("清除全部异常", new
		{
			clearPoison = true,
			clearFire = true
		});
	}

	public Task ClearStatusAsync()
	{
		return ClearAllStatusAsync();
	}

	public Task RestoreAllAsync()
	{
		return MutateVitals("一键补满状态", new
		{
			restoreHealth = true,
			restoreFullness = true,
			clearPoison = true,
			clearFire = true
		});
	}

	public Task ReviveAsync()
	{
		return RunSelected("复活成员", "players.revive", (RoomMemberSnapshot member) => new
		{
			playerKey = member.Key
		});
	}

	public Task RestoreRoomAsync(string operation)
	{
		return Run(async (IGameRuntimeClient runtime) =>
		{
			MemberVitalsRequest.FromOperation(operation);
			BatchResults.Clear();
			RuntimeCommandResult runtimeCommandResult = await Send(runtime, "players.vitals.batch", new { operation });
			if (runtimeCommandResult.Succeeded)
			{
				JsonElement? applied = runtimeCommandResult.Applied;
				if (applied.HasValue && applied.GetValueOrDefault().TryGetProperty("results", out var value) && value.ValueKind == JsonValueKind.Array)
				{
					int num = 0;
					int num2 = 0;
					int num3 = 0;
					foreach (JsonElement item in value.EnumerateArray())
					{
						string text = Text(item, "displayName");
						string text2 = Text(item, "reason");
						if (Bool(item, "succeeded") && item.TryGetProperty("snapshot", out var value2) && Bool(value2, "verified"))
						{
							RoomMemberSnapshot updated = Parse(value2);
							int num4 = Members.ToList().FindIndex((RoomMemberSnapshot member) => member.Key == updated.Key);
							if (num4 >= 0)
							{
								Members[num4] = updated;
							}
							else
							{
								Members.Add(updated);
							}
							if (SelectedMember?.Key == updated.Key)
							{
								SelectedMember = updated;
							}
							num++;
							BatchResults.Add("✓ " + text + " · " + text2);
						}
						else if (Bool(item, "skipped"))
						{
							num2++;
							BatchResults.Add("— " + text + " · " + text2);
						}
						else
						{
							num3++;
							BatchResults.Add("! " + text + " · " + (string.IsNullOrEmpty(text2) ? "未收到可靠状态读回" : text2));
						}
					}
					NotifyCatalogChanged();
					Status = $"全房补给 · 已读回 {num} 人 / 跳过 {num2} 人 / 失败 {num3} 人";
					if (value.GetArrayLength() == 0)
					{
						Status = "房间暂无可操作成员，请刷新列表";
					}
					if (num > 0 && num3 == 0)
					{
						LastVerifiedOperation = $"全房补给 · {num} 人已读回，{num2} 人跳过";
						LastVerifiedAt = DateTime.Now.ToString("HH:mm:ss");
					}
					return;
				}
			}
			throw new InvalidOperationException(runtimeCommandResult.Message);
		});
	}

	public Task ResetPoliciesAsync()
	{
		return Run(async (IGameRuntimeClient runtime) =>
		{
			RuntimeCommandResult runtimeCommandResult = await Send(runtime, "players.policies.reset", new { });
			if (runtimeCommandResult.Succeeded)
			{
				JsonElement? applied = runtimeCommandResult.Applied;
				if (applied.HasValue)
				{
					JsonElement valueOrDefault = applied.GetValueOrDefault();
					if (Bool(valueOrDefault, "disabled"))
					{
						string text = (RareScope = "off");
						string text3 = (DropScope = text);
						string text5 = (FireScope = text3);
						string text7 = (PoisonScope = text5);
						string healthScope = (FullnessScope = text7);
						HealthScope = healthScope;
						PolicyStateLabel = "全部成员策略已关闭";
						Status = "已停止六项成员策略；不影响常用修改中的独立开关，也不回退已恢复的生命和饱食度";
						return;
					}
				}
			}
			throw new InvalidOperationException("运行时未确认策略已关闭。" + runtimeCommandResult.Message);
		});
	}

	public Task ApplyPoliciesAsync()
	{
		return RunSelectedPair(async (IGameRuntimeClient runtime, RoomMemberSnapshot member) =>
		{
			RuntimeCommandResult runtimeCommandResult = await Send(runtime, "players.policies.set", new
			{
				playerKey = member.Key,
				health = NormalizeScope(HealthScope),
				fullness = NormalizeScope(FullnessScope),
				poison = NormalizeScope(PoisonScope),
				fire = NormalizeScope(FireScope),
				drop = NormalizeScope(DropScope),
				rare = NormalizeScope(RareScope)
			});
			if (!runtimeCommandResult.Succeeded)
			{
				throw new InvalidOperationException(runtimeCommandResult.Message);
			}
			PolicyStateLabel = "等待实际事件验证";
			Status = "成员策略已写入运行时，将在死亡或钓获事件发生后验证";
		});
	}

	public Task TeleportAsync(string mode)
	{
		return RunSelectedPair(async (IGameRuntimeClient runtime, RoomMemberSnapshot member) =>
		{
			RuntimeCommandResult runtimeCommandResult = await Send(runtime, "players.teleport", new
			{
				playerKey = member.Key,
				mode = mode
			});
			if (!runtimeCommandResult.Succeeded)
			{
				throw new InvalidOperationException(runtimeCommandResult.Message);
			}
			Status = runtimeCommandResult.Message;
		});
	}

	private Task MutateVitals(string operation, object changes)
	{
		return RunSelectedPair(async (IGameRuntimeClient runtime, RoomMemberSnapshot member) =>
		{
			using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(changes));
			Dictionary<string, object> dictionary = new Dictionary<string, object> { ["playerKey"] = member.Key };
			foreach (JsonProperty item in document.RootElement.EnumerateObject())
			{
				dictionary[item.Name] = item.Value.GetBoolean();
			}
			RuntimeCommandResult result = await Send(runtime, "players.vitals.set", dictionary);
			ApplySelectedReadBack(result, operation);
		});
	}

	private Task RunSelected(string operation, string id, Func<RoomMemberSnapshot, object> arguments)
	{
		return RunSelectedPair(async (IGameRuntimeClient runtime, RoomMemberSnapshot member) =>
		{
			RuntimeCommandResult result = await Send(runtime, id, arguments(member));
			ApplySelectedReadBack(result, operation);
		});
	}

	private Task RunSelectedPair(Func<IGameRuntimeClient, RoomMemberSnapshot, Task> action)
	{
		RoomMemberSnapshot member = SelectedMember;
		if ((object)member == null)
		{
			Status = "请先选择房间成员";
			return Task.CompletedTask;
		}
		return Run((IGameRuntimeClient runtime) => action(runtime, member));
	}

	private void OpenSelectedInTeleport()
	{
		if ((object)SelectedMember == null)
		{
			Status = "请先选择房间成员";
		}
		else
		{
			_openInTeleport?.Invoke(SelectedMember);
		}
	}

	private async Task Run(Func<IGameRuntimeClient, Task> action)
	{
		if (IsBusy)
		{
			return;
		}
		if (!_sponsorAccessGate.TryAuthorize("players.catalog"))
		{
			Status = "房间成员控制台需要激活赞助版";
			return;
		}
		if (_runtime == null || !_capabilities.CanExecute(Capability.HostWorld))
		{
			Status = "此操作仅房主或单人世界可用";
			return;
		}
		long epoch = _connectionEpoch;
		IsBusy = true;
		try
		{
			await action(_runtime);
		}
		catch (Exception ex)
		{
			if (epoch == _connectionEpoch)
			{
				Status = "成员操作失败 · " + ex.Message;
			}
		}
		finally
		{
			if (epoch == _connectionEpoch)
			{
				IsBusy = false;
			}
		}
	}

	private async Task<RuntimeCommandResult> Send(IGameRuntimeClient runtime, string id, object arguments)
	{
		long epoch = _connectionEpoch;
		if (runtime != _runtime || !_sponsorAccessGate.TryAuthorize(id))
		{
			throw new InvalidOperationException("连接或赞助授权已变化，未发送操作。");
		}
		RuntimeCommandResult result = await runtime.ExecuteAsync(new FeatureCommand(id, JsonSerializer.SerializeToElement(arguments), Capability.HostWorld), CancellationToken.None);
		if (epoch != _connectionEpoch || runtime != _runtime)
		{
			throw new OperationCanceledException("已丢弃旧房间返回的数据。");
		}
		return result;
	}

	private void ApplySelectedReadBack(RuntimeCommandResult result, string operation)
	{
		if (result.Succeeded)
		{
			JsonElement? applied = result.Applied;
			if (applied.HasValue)
			{
				JsonElement valueOrDefault = applied.GetValueOrDefault();
				RoomMemberSnapshot updated = Parse(valueOrDefault);
				int num = Members.ToList().FindIndex((RoomMemberSnapshot member) => member.Key == updated.Key);
				if (num >= 0)
				{
					Members[num] = updated;
				}
				SelectedMember = updated;
				NotifyCatalogChanged();
				LastVerifiedOperation = operation + " · " + updated.DisplayName;
				LastVerifiedAt = DateTime.Now.ToString("HH:mm:ss");
				Status = result.Message;
				return;
			}
		}
		throw new InvalidOperationException(result.Message);
	}

	private void NotifyCatalogChanged()
	{
		Changed("FilteredMembers");
		Changed("OnlineCount");
		Changed("AliveCount");
		Changed("DownCount");
	}

	private static object PolicyArguments(string scope, string playerKey)
	{
		bool flag = ((scope == "all" || scope == "selected") ? true : false);
		return new
		{
			scope = (flag ? scope : "local"),
			playerKey = ((scope == "selected") ? playerKey : null)
		};
	}

	private static string NormalizeScope(string scope)
	{
		bool flag;
		switch (scope)
		{
		case "local":
		case "selected":
		case "all":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return "off";
		}
		return scope;
	}

	public void RefreshSponsorAccess()
	{
		Status = (_sponsorAccessGate.IsSponsor ? Status : "房间成员控制台为赞助版专用功能");
		Changed("CanManage");
	}

	private static RoomMemberSnapshot Parse(JsonElement value)
	{
		return new RoomMemberSnapshot(Text(value, "key"), Text(value, "displayName"), Text(value, "ownerTail"), Bool(value, "isLocal"), Bool(value, "isHost"), Bool(value, "alive"), Number(value, "health"), Number(value, "fullness"), Number(value, "poison"), Number(value, "fire"), Number(value, "x"), Number(value, "y"), Number(value, "z"));
	}

	private static string Text(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out var value2))
		{
			return string.Empty;
		}
		return value2.GetString() ?? string.Empty;
	}

	private static bool Bool(JsonElement value, string name)
	{
		if (value.TryGetProperty(name, out var value2))
		{
			return value2.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private static double Number(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out var value2) || !value2.TryGetDouble(out var value3))
		{
			return 0.0;
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
		Changed(name);
		return true;
	}

	private void Changed([CallerMemberName] string? name = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
