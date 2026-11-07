using System;
using System.Windows;
using System.Windows.Threading;

namespace HowToFishTrainer.App;
public partial class MainWindow
{
    private StatusWidget? _statusWidget;
    private DispatcherTimer? _experienceTimer;
    private bool _experienceTickBusy;
    private void InitializeExperienceUi()
    {
        _statusWidget=new StatusWidget(_viewModel,this,()=>_attachedProcessId,OpenJournal);
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
}
