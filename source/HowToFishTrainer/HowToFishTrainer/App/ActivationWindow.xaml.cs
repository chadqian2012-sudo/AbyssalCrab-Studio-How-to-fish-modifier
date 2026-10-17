using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using HowToFishTrainer.App.Distribution;
using HowToFishTrainer.App.Licensing;

namespace HowToFishTrainer.App;

public partial class ActivationWindow : Window, IComponentConnector
{
	private readonly ILicenseClient _license;

	public ActivationWindow(ILicenseClient license)
	{
		_license = license;
		InitializeComponent();
		PurchaseHost.Content = null;
		PurchaseHost.Visibility = Visibility.Collapsed;
		Render(_license.Current);
	}

	private async void Activate_Click(object sender, RoutedEventArgs e)
	{
		SetBusy(busy: true);
		ActionMessageText.Text = "正在通过 HTTPS 连接授权服务器…";
		try
		{
			LicenseActivationResult licenseActivationResult = await _license.ActivateAsync(ActivationCodeBox.Text);
			Render(licenseActivationResult.Status);
			ActionMessageText.Text = (licenseActivationResult.Success ? "✓ 激活成功，赞助功能已即时解锁。" : licenseActivationResult.Status.Message);
			ActionMessageText.Foreground = (Brush)FindResource(licenseActivationResult.Success ? "SuccessBrush" : "DangerBrush");
			if (licenseActivationResult.Success)
			{
				ActivationCodeBox.Clear();
			}
		}
		catch (Exception ex)
		{
			ActionMessageText.Text = "激活失败：" + ex.Message;
			ActionMessageText.Foreground = (Brush)FindResource("DangerBrush");
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private async void Refresh_Click(object sender, RoutedEventArgs e)
	{
		SetBusy(busy: true);
		try
		{
			LicenseStatus licenseStatus = await _license.RefreshAsync();
			Render(licenseStatus);
			ActionMessageText.Text = licenseStatus.Message;
			ActionMessageText.Foreground = (Brush)FindResource(licenseStatus.IsSponsor ? "SuccessBrush" : "MutedBrush");
		}
		catch (Exception ex)
		{
			ActionMessageText.Text = "重新校验失败：" + ex.Message;
			ActionMessageText.Foreground = (Brush)FindResource("DangerBrush");
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private async void Clear_Click(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show(this, "此操作只清除当前电脑保存的授权，不会解除服务器设备绑定。确定继续吗？", "清除本机授权", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		SetBusy(busy: true);
		try
		{
			LicenseStatus licenseStatus = await _license.ClearLocalAsync();
			Render(licenseStatus);
			ActionMessageText.Text = licenseStatus.Message;
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private void Render(LicenseStatus status)
	{
		StatusTitleText.Text = status.Title;
		StatusMessageText.Text = status.Message;
		string deviceId = _license.DeviceId;
		TextBlock deviceText = DeviceText;
		string text;
		if (deviceId.Length <= 16)
		{
			text = deviceId;
		}
		else
		{
			int length = deviceId.Length;
			int num = length - 16;
			text = "…" + deviceId.Substring(num, length - num);
		}
		deviceText.Text = text;
		TextBlock statusIcon = StatusIcon;
		string text2 = ((!status.IsSponsor) ? (status.State switch
		{
			LicenseState.Invalid => "!", 
			LicenseState.DeviceLimit => "×", 
			LicenseState.ServiceUnavailable => "…", 
			_ => "◇", 
		}) : "✓");
		statusIcon.Text = text2;
		Border statusAccent = StatusAccent;
		if (status.IsSponsor)
		{
			text2 = "SuccessBrush";
		}
		else
		{
			LicenseState state = status.State;
			bool flag = (uint)(state - 3) <= 1u;
			text2 = (flag ? "DangerBrush" : "AccentBrush");
		}
		statusAccent.Background = (Brush)FindResource(text2);
		ActivateButton.Content = (status.IsSponsor ? "更换激活码" : "激活赞助版");
	}

	private void SetBusy(bool busy)
	{
		ActivateButton.IsEnabled = !busy;
		RefreshButton.IsEnabled = !busy;
		ClearButton.IsEnabled = !busy;
		ActivationCodeBox.IsEnabled = !busy;
	}

	private void ReportPurchaseMessage(string message, bool isError)
	{
		ActionMessageText.Text = message;
		ActionMessageText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "MutedBrush");
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
