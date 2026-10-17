using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HowToFishTrainer.App;

public sealed class StatusWidget : Window, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly Window _main;
    private readonly Func<int?> _processId;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    public StatusWidget(MainViewModel vm, Window main, Func<int?> processId)
    {
        _vm=vm;_main=main;_processId=processId;DataContext=vm;
        Width=330;SizeToContent=SizeToContent.Height;MinHeight=120;MaxHeight=300;
        ShowInTaskbar=false;ShowActivated=false;Topmost=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;Title="AbyssalCrab Studio · 游戏状态";
        Left=Math.Clamp(vm.WidgetLeft,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-Width));
        Top=Math.Clamp(vm.WidgetTop,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-300));
        var border=new Border{CornerRadius=new CornerRadius(16),Padding=new Thickness(14),BorderThickness=new Thickness(1)};
        border.SetResourceReference(Border.BackgroundProperty,"GlassCardBrush");border.SetResourceReference(Border.BorderBrushProperty,"GlassEdgeChromeBrush");
        var panel=new StackPanel();border.Child=panel;Content=border;
        var heading=new TextBlock{Text="AbyssalCrab Studio · 拖动移动",FontWeight=FontWeights.SemiBold,FontSize=12,Cursor=Cursors.SizeAll};
        heading.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");heading.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed){DragMove();vm.WidgetLeft=Left;vm.WidgetTop=Top;}};panel.Children.Add(heading);
        AddText(panel,"ConnectionStatus",10);AddText(panel,"ActiveFeaturesText",11);AddText(panel,"ActivePresetName",10);AddText(panel,"PageTitle",10);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};
        var open=new Button{Content="打开面板",Padding=new Thickness(10,4,10,4)};open.Click+=(_,_)=>{main.Show();main.WindowState=WindowState.Normal;main.Activate();};buttons.Children.Add(open);
        var hide=new Button{Content="隐藏组件",Padding=new Thickness(10,4,10,4),Margin=new Thickness(8,0,0,0)};hide.Click+=(_,_)=>vm.WidgetVisible=false;buttons.Children.Add(hide);panel.Children.Add(buttons);
        AddText(panel,"",9,"紧急停用：Ctrl + Shift + F12");
        _timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};_timer.Tick+=Tick;
        if(!Array.Exists(Environment.GetCommandLineArgs(),arg=>arg.Equals("--ui-preview",StringComparison.OrdinalIgnoreCase)))_timer.Start();
    }
    private static void AddText(Panel panel,string path,int size,string? literal=null)
    {
        var text=new TextBlock{FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
        text.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");if(literal!=null)text.Text=literal;else text.SetBinding(TextBlock.TextProperty,new Binding(path));panel.Children.Add(text);
    }
    private void Tick(object? sender,EventArgs args)
    {
        if(_disposed)return;
        var front=NativeOverlay.GetForegroundWindow();NativeOverlay.GetWindowThreadProcessId(front,out uint pid);
        bool visible=_vm.WidgetVisible&&(_main.IsActive||IsActive||_processId()==pid);
        if(visible&&!IsVisible)Show();else if(!visible&&IsVisible)Hide();
    }
    protected override void OnClosing(CancelEventArgs args)
    {
        // Alt+F4 hides this reusable component; only owner disposal destroys it.
        if(!_disposed){args.Cancel=true;_vm.WidgetVisible=false;Hide();}
        base.OnClosing(args);
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_timer.Stop();_timer.Tick-=Tick;Close();}
}
