using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HowToFishTrainer.App;

public partial class FeatureSearchBox : UserControl
{
    internal Func<SearchHit, bool>? RevealResult { get; set; }
    internal Action? Dismissed { get; set; }

    public FeatureSearchBox()
    {
        InitializeComponent();
        TextCompositionManager.AddPreviewTextInputStartHandler(FeatureSearch, (_, _) => IsComposingQuery = true);
        TextCompositionManager.AddPreviewTextInputHandler(FeatureSearch, (_, _) => IsComposingQuery = false);
        FeatureSearch.LostKeyboardFocus += (_, _) => IsComposingQuery = false;
    }

    internal bool IsComposingQuery { get; private set; }

    internal void ShowSearch()
    {
        Visibility = Visibility.Visible;
        SearchHint.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
        FeatureSearch.Focus();
        FeatureSearch.SelectAll();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Dismissed?.Invoke();

    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SearchHit hit }) Open(hit);
    }

    private void Open(SearchHit hit)
    {
        if (DataContext is not MainViewModel vm || vm.SelectSearchPage(hit) is not { } current) return;
        Dismissed?.Invoke();
        if (RevealResult?.Invoke(current) == true) return;
        ShowSearch();
        SearchHint.SetCurrentValue(TextBlock.TextProperty, $"已打开分类，暂时找不到「{current.Title}」的设置控件。");
    }

    internal bool HandleWindowKey(KeyEventArgs e)
    {
        if (IsComposingQuery) return false;
        if (e.Key == Key.F && e.KeyboardDevice.Modifiers == ModifierKeys.Control) ShowSearch();
        else if (e.Key == Key.Escape && Visibility == Visibility.Visible) Dismissed?.Invoke();
        else return false;
        e.Handled = true;
        return true;
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsComposingQuery || HandleWindowKey(e)) return;
        if (e.KeyboardDevice.Modifiers != ModifierKeys.None || FeatureSearchResults.Items.Count == 0) return;
        if (e.Key is Key.Down or Key.Up)
        {
            int last = FeatureSearchResults.Items.Count - 1;
            int index = FeatureSearchResults.SelectedIndex;
            index = index < 0 ? (e.Key == Key.Down ? 0 : last) : Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, last);
            FeatureSearchResults.SelectedIndex = index;
            FeatureSearchResults.ScrollIntoView(FeatureSearchResults.SelectedItem);
            FeatureSearch.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (ReferenceEquals(e.OriginalSource, FeatureSearch) || e.OriginalSource is ListBoxItem or ListBox))
        {
            if ((FeatureSearchResults.SelectedItem ?? FeatureSearchResults.Items[0]) is SearchHit hit) Open(hit);
            e.Handled = true;
        }
    }
}
