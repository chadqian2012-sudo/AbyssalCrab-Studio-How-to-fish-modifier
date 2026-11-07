using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace HowToFishTrainer.App;
public partial class FishingWindow : Window
{
    private readonly FishingViewModel _model;
    private readonly bool _ownsModel;
    public FishingWindow(FishingViewModel model, int tab = 0, bool ownsModel = false)
    { _model = model; _ownsModel = ownsModel; InitializeComponent(); DataContext = model; Tabs.SelectedIndex = tab; Loaded += LoadedWindow; }
    private async void LoadedWindow(object sender, RoutedEventArgs e)
    { Loaded -= LoadedWindow; await _model.RefreshBackupsAsync(); await _model.ReadCodexAsync(); }
    public void SelectTab(int tab) { Tabs.SelectedIndex = tab; }
    private void Finish_Click(object sender, RoutedEventArgs e) { _model.FinishSession("手动结束"); Tabs.SelectedIndex = 2; }
    private void ChooseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 How to Fish 的 Saves 目录" };
        if (dialog.ShowDialog(this) == true) _model.SaveRoot = dialog.FolderName;
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedBackup == null || !_model.CanEdit) return;
        if (MessageBox.Show(this, $"恢复此备份？\n\n{_model.SelectedBackup.Display}\n\n目标：{_model.SaveRoot}\n\n同名存档将被覆盖，其余存档保留。恢复前会自动保存当前磁盘存档副本。请完全退出游戏。", "确认恢复存档", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            await _model.RestoreSelectedAsync();
    }
    private void DeleteCatch_Click(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedCatch != null && MessageBox.Show(this, "删除选中的鱼获记录？统计会重新计算。", "删除日志", MessageBoxButton.OKCancel) == MessageBoxResult.OK) _model.DeleteSelectedCatch();
    }
    private void ExportAll_Click(object sender, RoutedEventArgs e) => ExportCsv(true);
    private void ExportSession_Click(object sender, RoutedEventArgs e) => ExportCsv(false);
    private void ExportCsv(bool all)
    {
        var dialog = new SaveFileDialog { Title = "导出鱼获记录", Filter = "CSV 表格|*.csv", DefaultExt = ".csv", FileName = $"AbyssalCrab-{(all ? "钓鱼日志" : "本局回顾")}-{DateTime.Now:yyyyMMdd-HHmm}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { var encoding = new UTF8Encoding(true); FishingDataStore.WriteAtomic(dialog.FileName, encoding.GetPreamble().ConcatBytes(encoding.GetBytes(_model.ExportCsv(all)))); }
        catch (Exception error) { MessageBox.Show(this, "导出失败：" + error.Message); }
    }
    internal static byte[] RenderRecap(FishingViewModel model, Brush background, Brush foreground)
    {
        var panel = new Border { Width = 1100, Background = background, Padding = new Thickness(55), CornerRadius = new CornerRadius(24) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "AbyssalCrab Studio · 本局回顾", Foreground = foreground, FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 24) });
        stack.Children.Add(new TextBlock { Text = model.Summary, Foreground = foreground, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 20, LineHeight = 32, TextWrapping = TextWrapping.Wrap });
        panel.Child = stack; panel.Measure(new Size(1100, double.PositiveInfinity)); int height = Math.Max(760, (int)Math.Ceiling(panel.DesiredSize.Height)); panel.Height = height; panel.Measure(new Size(1100, height)); panel.Arrange(new Rect(0, 0, 1100, height)); panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1100, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var memory = new MemoryStream(); encoder.Save(memory); return memory.ToArray();
    }
    private void ExportImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "保存每局回顾图片", Filter = "PNG 图片|*.png", DefaultExt = ".png", FileName = $"AbyssalCrab-本局回顾-{DateTime.Now:yyyyMMdd-HHmm}.png" };
        if (dialog.ShowDialog(this) != true) return;
        try { FishingDataStore.WriteAtomic(dialog.FileName, RenderRecap(_model, (Brush)FindResource("GlassCanvasBrush"), (Brush)FindResource("TextBrush"))); }
        catch (Exception error) { MessageBox.Show(this, "图片导出失败：" + error.Message); }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_model.Busy) { e.Cancel = true; MessageBox.Show(this, "正在读取、备份或恢复，请等待完成后关闭。", "任务进行中"); }
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e) { if (_ownsModel) _model.Dispose(); base.OnClosed(e); }
}
internal static class JournalBytes
{
    internal static byte[] ConcatBytes(this byte[] first, byte[] second) { var result = new byte[first.Length + second.Length]; first.CopyTo(result, 0); second.CopyTo(result, first.Length); return result; }
}
