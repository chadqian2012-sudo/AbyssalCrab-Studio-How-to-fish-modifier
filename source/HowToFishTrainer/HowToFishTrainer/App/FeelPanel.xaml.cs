using System;
using System.Windows;
using System.Windows.Controls;
namespace HowToFishTrainer.App;
public partial class FeelPanel : UserControl
{
    public FeelPanel() { InitializeComponent(); }
    private void FeelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        // Binding/realization must not rewrite numeric drafts. Only an explicit slider gesture edits input.
        if(sender is Slider slider && (slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin) && slider.DataContext is FeatureItemViewModel feature)
            feature.ValueInput.Value=feature.ValueInput.IntegerOnly?Math.Round(args.NewValue):Math.Round(args.NewValue,3);
    }
}
