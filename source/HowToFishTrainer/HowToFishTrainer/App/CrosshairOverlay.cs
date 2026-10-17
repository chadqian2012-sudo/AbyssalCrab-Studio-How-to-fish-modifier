using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace HowToFishTrainer.App;

internal sealed class CrosshairOverlay : IDisposable
{
	private sealed class OverlayWindow : Window
	{
		public CrosshairGlyph Glyph { get; }

		public OverlayWindow(CrosshairViewModel model)
		{
			Width = (Height = 64.0);
			WindowStyle = WindowStyle.None;
			ResizeMode = ResizeMode.NoResize;
			AllowsTransparency = true;
			Background = Brushes.Transparent;
			ShowInTaskbar = false;
			ShowActivated = false;
			Topmost = true;
			Focusable = false;
			IsHitTestVisible = false;
			Content = (Glyph = new CrosshairGlyph(model));
			SourceInitialized += (object? _, EventArgs _) =>
			{
				nint handle = new WindowInteropHelper(this).Handle;
				long num2 = ((IntPtr)NativeOverlay.GetWindowLongPtr(handle, -20)).ToInt64();
				NativeOverlay.SetWindowLongPtr(handle, -20, new IntPtr(num2 | 0x20 | 0x80 | 0x8000000));
				HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
			};
		}

		private static nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
		{
			switch (message)
			{
			case 132:
				handled = true;
				return new IntPtr(-1);
			case 33:
				handled = true;
				return new IntPtr(3);
			default:
				return 0;
			}
		}
	}

	private sealed class CrosshairGlyph(CrosshairViewModel model) : FrameworkElement
	{
		protected override void OnRender(DrawingContext dc)
		{
			base.OnRender(dc);
			Point center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);
			Brush brush = model.Color switch
			{
				"绿色" => (Brush)Brushes.LimeGreen, 
				"紫色" => Brushes.Violet, 
				"红色" => Brushes.OrangeRed, 
				_ => Brushes.White, 
			};
			Pen pen = new Pen(Brushes.Black, 4.0);
			Pen pen2 = new Pen(brush, 2.0);
			double num = model.Size / 2.0;
			if (model.Shape == "圆点")
			{
				dc.DrawEllipse(Brushes.Black, null, center, 3.5, 3.5);
				dc.DrawEllipse(brush, null, center, 2.0, 2.0);
				return;
			}
			if (model.Shape == "圆环")
			{
				dc.DrawEllipse(null, pen, center, num, num);
				dc.DrawEllipse(null, pen2, center, num, num);
				return;
			}
			Pen[] array = new Pen[2] { pen, pen2 };
			foreach (Pen pen3 in array)
			{
				dc.DrawLine(pen3, new Point(center.X - num - 3.0, center.Y), new Point(center.X - 3.0, center.Y));
				dc.DrawLine(pen3, new Point(center.X + 3.0, center.Y), new Point(center.X + num + 3.0, center.Y));
				dc.DrawLine(pen3, new Point(center.X, center.Y - num - 3.0), new Point(center.X, center.Y - 3.0));
				dc.DrawLine(pen3, new Point(center.X, center.Y + 3.0), new Point(center.X, center.Y + num + 3.0));
			}
		}
	}

	private readonly CrosshairViewModel _model;

	private readonly Func<int?> _processId;

	private readonly DispatcherTimer _timer;

	private OverlayWindow? _window;

	private bool _reading;

	private bool _disposed;

	private long _lastRead;

	public CrosshairOverlay(CrosshairViewModel model, Func<int?> processId, Dispatcher dispatcher)
	{
		_model = model;
		_processId = processId;
		_model.PropertyChanged += Changed;
		_timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33.0), DispatcherPriority.Background, Tick, dispatcher);
		_timer.Start();
	}

	private void Changed(object? sender, PropertyChangedEventArgs e)
	{
		if (!_model.Enabled)
		{
			_window?.Hide();
		}
		bool flag;
		switch (e.PropertyName)
		{
		case "Shape":
		case "Size":
		case "Color":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			_window?.Glyph.InvalidateVisual();
		}
	}

	private async void Tick(object? sender, EventArgs e)
	{
		if (_disposed)
		{
			return;
		}
		if (!_model.Enabled || !TryGameBounds(out var _, out var _))
		{
			_window?.Hide();
			return;
		}
		if (_reading)
		{
			if (Stopwatch.GetElapsedTime(_lastRead).TotalMilliseconds > 250.0)
			{
				_window?.Hide();
			}
			return;
		}
		_reading = true;
		_lastRead = Stopwatch.GetTimestamp();
		try
		{
			AimProjection aimProjection = await _model.ReadAsync(CancellationToken.None);
			if (_disposed)
			{
				return;
			}
			if (!_model.Enabled || (object)aimProjection == null || Stopwatch.GetElapsedTime(_lastRead).TotalMilliseconds > 250.0 || !TryGameBounds(out var rect2, out var origin2))
			{
				_window?.Hide();
				return;
			}
			(int, int) tuple = CrosshairPlacement.Position(origin2.X, origin2.Y, rect2.Right, rect2.Bottom, aimProjection.X, aimProjection.Y);
			if (_window == null)
			{
				_window = new OverlayWindow(_model);
			}
			if (!_window.IsVisible)
			{
				_window.Show();
			}
			nint handle = new WindowInteropHelper(_window).Handle;
			uint dpiForWindow = NativeOverlay.GetDpiForWindow(handle);
			int num = (int)Math.Round((double)(64 * Math.Max(96u, dpiForWindow)) / 96.0);
			if (!NativeOverlay.SetWindowPos(handle, new IntPtr(-1), tuple.Item1 - num / 2, tuple.Item2 - num / 2, num, num, 80u))
			{
				_window.Hide();
				_model.SetStatus("无法定位外置窗口，请检查游戏窗口模式");
			}
			else
			{
				_model.SetStatus("显示中 · " + aimProjection.Mode);
			}
		}
		catch (Exception)
		{
			_window?.Hide();
			if (!_disposed && _model.Enabled)
			{
				_model.SetStatus("瞄准数据暂不可用，准星已隐藏；等待连接恢复");
			}
		}
		finally
		{
			_reading = false;
		}
	}

	private bool TryGameBounds(out NativeRect rect, out NativePoint origin)
	{
		rect = default;
		origin = default;
		nint foregroundWindow = NativeOverlay.GetForegroundWindow();
		NativeOverlay.GetWindowThreadProcessId(foregroundWindow, out var processId);
		int? num = _processId();
		if (num.HasValue)
		{
			int valueOrDefault = num.GetValueOrDefault();
			if (processId == valueOrDefault && !NativeOverlay.IsIconic(foregroundWindow) && NativeOverlay.GetClientRect(foregroundWindow, out rect) && rect.Right > 0 && rect.Bottom > 0)
			{
				return NativeOverlay.ClientToScreen(foregroundWindow, ref origin);
			}
		}
		return false;
	}

	public void Dispose()
	{
		_disposed = true;
		_timer.Stop();
		_model.PropertyChanged -= Changed;
		_window?.Close();
		_window = null;
	}
}
