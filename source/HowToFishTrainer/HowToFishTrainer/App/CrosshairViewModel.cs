using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed class CrosshairViewModel : INotifyPropertyChanged
{
	private IGameRuntimeClient? _runtime;

	private int _epoch;

	private bool _enabled;

	private string _shape = "十字";

	private string _color = "白色";

	private string _status = "关闭 · 仅在游戏前台显示";

	private double _size = 16.0;

	public string[] Shapes { get; } = new string[3] { "十字", "圆点", "圆环" };

	public string[] Colors { get; } = new string[4] { "白色", "绿色", "紫色", "红色" };

	public bool Enabled
	{
		get
		{
			return _enabled;
		}
		set
		{
			if (Set(ref _enabled, value, "Enabled"))
			{
				_epoch++;
				Status = (value ? "等待游戏前台及有效瞄准数据" : "外置准星已关闭");
			}
		}
	}

	public string Shape
	{
		get
		{
			return _shape;
		}
		set
		{
			if (Shapes.Contains(value))
			{
				Set(ref _shape, value, "Shape");
			}
		}
	}

	public string Color
	{
		get
		{
			return _color;
		}
		set
		{
			if (Colors.Contains(value))
			{
				Set(ref _color, value, "Color");
			}
		}
	}

	public double Size
	{
		get
		{
			return _size;
		}
		set
		{
			if (double.IsFinite(value))
			{
				Set(ref _size, Math.Clamp(value, 6.0, 36.0), "Size");
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

	public event PropertyChangedEventHandler? PropertyChanged;

	public void Connect(IGameRuntimeClient runtime)
	{
		_runtime = runtime;
		_epoch++;
	}

	public void Disconnect()
	{
		_runtime = null;
		_epoch++;
		Enabled = false;
		Status = "连接已断开，准星已关闭";
	}

	public void SetStatus(string text)
	{
		Status = text;
	}

	public async Task<AimProjection?> ReadAsync(CancellationToken cancellationToken)
	{
		IGameRuntimeClient runtime = _runtime;
		int epoch = _epoch;
		if (!Enabled || runtime == null)
		{
			return null;
		}
		RuntimeCommandResult runtimeCommandResult = await runtime.ExecuteAsync(new FeatureCommand("view.aim.read", JsonSerializer.SerializeToElement(new { }), Capability.ReadOnly), cancellationToken);
		if (epoch != _epoch || runtime != _runtime || !Enabled)
		{
			return null;
		}
		if (runtimeCommandResult.Succeeded)
		{
			JsonElement? applied = runtimeCommandResult.Applied;
			if (applied.HasValue)
			{
				JsonElement valueOrDefault = applied.GetValueOrDefault();
				if (valueOrDefault.TryGetProperty("visible", out var value) && value.ValueKind == JsonValueKind.True)
				{
					if (!valueOrDefault.TryGetProperty("x", out var value2) || value2.ValueKind != JsonValueKind.Number || !value2.TryGetDouble(out var value3) || !valueOrDefault.TryGetProperty("y", out var value4) || value4.ValueKind != JsonValueKind.Number || !value4.TryGetDouble(out var value5) || !CrosshairPlacement.IsValid(value3, value5))
					{
						return null;
					}
					string mode = ((valueOrDefault.TryGetProperty("mode", out var value6) && value6.ValueKind == JsonValueKind.String) ? value6.GetString() : "方向参考");
					return new AimProjection(value3, value5, mode);
				}
			}
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
}
