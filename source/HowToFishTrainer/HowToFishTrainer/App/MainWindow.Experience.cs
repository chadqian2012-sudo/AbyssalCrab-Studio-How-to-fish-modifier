using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HowToFishTrainer.App;
public partial class MainWindow
{
    private StatusWidget? _statusWidget;
    private DispatcherTimer? _experienceTimer;
    private bool _experienceTickBusy;
    private void InitializeExperienceUi()
    {
        _statusWidget=new StatusWidget(_viewModel,this,()=>_attachedProcessId);
        _experienceTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(500)};
        _experienceTimer.Tick+=ExperienceTick;
        if(!Array.Exists(Environment.GetCommandLineArgs(),arg=>arg.Equals("--ui-preview",StringComparison.OrdinalIgnoreCase)))_experienceTimer.Start();
    }
    private async void ExperienceTick(object? sender,EventArgs args)
    {
        if(_experienceTickBusy||_appearanceClosing||_refreshingMode||_attaching)return;
        _experienceTickBusy=true;try{await _viewModel.TickExperienceAsync(DateTimeOffset.UtcNow);}finally{_experienceTickBusy=false;}
    }
    private void CloseExperienceUi()
    {
        _experienceTimer?.Stop();if(_experienceTimer!=null)_experienceTimer.Tick-=ExperienceTick;
        _statusWidget?.Dispose();_viewModel.CloseExperience();
    }
    private void ExportTeleport_Click(object sender,RoutedEventArgs args)
    {
        var dialog=new SaveFileDialog{Title="导出传送点",Filter="JSON 文件|*.json",FileName="AbyssalCrab-传送点.json",DefaultExt=".json"};
        if(dialog.ShowDialog(this)!=true)return;
        try{AtomicLocalFile.Write(dialog.FileName,_viewModel.ExportTeleportPoints(),text=>System.Text.Json.JsonDocument.Parse(text).Dispose());_viewModel.PositionStatus="传送点与标记已导出";}
        catch(Exception e){_viewModel.PositionStatus="导出失败 · "+e.Message;}
    }
    private void ImportTeleport_Click(object sender,RoutedEventArgs args)
    {
        var dialog=new OpenFileDialog{Title="导入传送点（同名保留原值）",Filter="JSON 文件|*.json"};
        if(dialog.ShowDialog(this)!=true)return;
        try{if(new FileInfo(dialog.FileName).Length>4*1024*1024)throw new InvalidDataException("传送点文件超过 4 MB");_viewModel.ImportTeleportPoints(File.ReadAllText(dialog.FileName));}
        catch(Exception e){_viewModel.PositionStatus="导入失败，现有坐标保持不变 · "+e.Message;}
    }
}
