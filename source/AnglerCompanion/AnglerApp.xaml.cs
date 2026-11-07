using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
namespace HowToFishTrainer.App;
public partial class AnglerApp : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!SystemParameters.ClientAreaAnimation) Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/MotionReduced.xaml", UriKind.Relative) });
        if ((RenderCapability.Tier >> 16) == 0) Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/MaterialSolid.xaml", UriKind.Relative) });
        bool preview = e.Args.Contains("--ui-preview", StringComparer.OrdinalIgnoreCase);
        var folder = preview ? null : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbyssalCrab Studio", "HowToFishTrainer");
        var model = new FishingViewModel(folder, preview ? null : SaveBackupService.DefaultSaveRoot, startTimer: !preview);
        MainWindow = new FishingWindow(model, ownsModel: true);
        if (preview) { MainWindow.ShowActivated = false; MainWindow.ShowInTaskbar = false; MainWindow.WindowStartupLocation = WindowStartupLocation.Manual; MainWindow.Left = -20000; MainWindow.Top = -20000; }
        MainWindow.Show();
    }
}
