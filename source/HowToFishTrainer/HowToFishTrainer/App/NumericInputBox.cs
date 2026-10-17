using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace HowToFishTrainer.App;

// Reuses the existing themed TextBox; validation occupies its own layout row.
public sealed class NumericInputBox : UserControl
{
    public static readonly DependencyProperty InputProperty = DependencyProperty.Register(
        nameof(Input), typeof(NumericInput), typeof(NumericInputBox));
    public NumericInput? Input
    {
        get => (NumericInput?)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }
    public NumericInputBox()
    {
        Focusable = false;
        VerticalContentAlignment = VerticalAlignment.Top;
        var panel = new StackPanel();
        var editor = new TextBox { VerticalAlignment = VerticalAlignment.Top };
        editor.SetBinding(TextBox.TextProperty, new Binding("Input.Text") { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        editor.SetBinding(ToolTipProperty, new Binding("Input.RangeDescription") { Source = this });
        editor.SetBinding(AutomationProperties.NameProperty, new Binding("Input.Label") { Source = this });
        editor.SetBinding(AutomationProperties.HelpTextProperty, new Binding("Input.Error") { Source = this });
        var message = new TextBlock { FontSize = 10, Margin = new Thickness(1, 3, 1, 0), TextWrapping = TextWrapping.Wrap };
        message.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        message.SetBinding(TextBlock.TextProperty, new Binding("Input.Error") { Source = this });
        message.SetBinding(VisibilityProperty, new Binding("Input.HasError") { Source = this, Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(editor); panel.Children.Add(message);
        Content = panel;
    }
}
