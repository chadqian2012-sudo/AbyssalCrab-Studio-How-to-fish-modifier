using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace HowToFishTrainer.App;
public partial class MainWindow
{
    private FishingViewModel? _fishingJournal;
    private FishingWindow? _fishingWindow;
    private void OpenJournal(int tab)
    {
        bool preview = Environment.GetCommandLineArgs().Contains("--ui-preview", StringComparer.OrdinalIgnoreCase);
        _fishingJournal ??= new FishingViewModel(preview ? null : ProductIdentity.AppDataDirectory, preview ? null : SaveBackupService.DefaultSaveRoot, startTimer: !preview);
        if (_fishingWindow != null) { _fishingWindow.SelectTab(tab); _fishingWindow.Activate(); return; }
        _fishingWindow = new FishingWindow(_fishingJournal, tab) { Owner = this };
        _fishingWindow.Closed += (_, _) => _fishingWindow = null;
        _fishingWindow.Show();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_fishingJournal?.Busy == true)
        { e.Cancel = true; MessageBox.Show(this, "钓鱼手记正在读取、备份或恢复，请等待完成后关闭。", "任务进行中"); }
        base.OnClosing(e);
    }
}
