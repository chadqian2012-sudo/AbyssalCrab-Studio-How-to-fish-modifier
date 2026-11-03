using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace HowToFishTrainer.App;

// The editable draft is never clamped, reformatted, or replaced while typing.
// Value keeps the last valid number; every submit path must check FirstError first.
public sealed class NumericInput : INotifyPropertyChanged
{
    private string _text;
    private double _value;
    private double _lastValidValue;
    private string _error;
    private static readonly PropertyChangedEventArgs TextChanged = new(nameof(Text));
    private static readonly PropertyChangedEventArgs ValueChanged = new(nameof(Value));
    private static readonly PropertyChangedEventArgs ErrorChanged = new(nameof(Error));
    private static readonly PropertyChangedEventArgs HasErrorChanged = new(nameof(HasError));
    public double LastValidValue => _lastValidValue;
    public string Label { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public bool IntegerOnly { get; }
    public string RangeDescription { get; }
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;
            _text = value;
            if (TryParse(out double number, out _error)) { _value = number; _lastValidValue = number; }
            Changed(nameof(Text)); Changed(nameof(Value)); Changed(nameof(Error)); Changed(nameof(HasError));
        }
    }
    public double Value
    {
        get => _value;
        set
        {
            string formatted = Format(value);
            bool notifyValueOnly = _text == formatted && !EqualityComparer<double>.Default.Equals(_value, value);
            _value = value;
            if (IsValidValue(value)) _lastValidValue = value;
            if (notifyValueOnly) Changed(nameof(Value));
            else Text = formatted;
        }
    }
    public string Error => _error;
    public bool HasError => _error.Length != 0;
    public bool IsValidValue(double value) => double.IsFinite(value)
        && (!Minimum.HasValue || value >= Minimum.Value)
        && (!Maximum.HasValue || value <= Maximum.Value)
        && (!IntegerOnly || value == Math.Truncate(value));
    public event PropertyChangedEventHandler? PropertyChanged;
    public NumericInput(string label, double value, double? minimum, double? maximum, bool integerOnly = false)
    {
        Label = label; Minimum = minimum; Maximum = maximum; IntegerOnly = integerOnly;
        RangeDescription = minimum.HasValue && maximum.HasValue
            ? $"{Label}：{Format(minimum.Value)}–{Format(maximum.Value)}，{(IntegerOnly ? "需要整数" : "支持小数")}"
            : $"{Label}：{(IntegerOnly ? "需要整数" : "支持小数")}";
        _value = value; _lastValidValue = value; _text = Format(value);
        TryParse(out _, out _error);
    }
    private bool TryParse(out double value, out string error)
    {
        error = string.Empty;
        if (!double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
            error = $"{Label}：请输入有效数字";
        else if (Minimum.HasValue && value < Minimum || Maximum.HasValue && value > Maximum)
            error = RangeDescription;
        else if (IntegerOnly && value != Math.Truncate(value))
            error = $"{Label}：需要整数，不能使用小数";
        return error.Length == 0;
    }
    public static string FirstError(params NumericInput[] inputs)
    { foreach(var input in inputs)if(input.HasError)return input.Error;return string.Empty; }
    public static string FirstError(NumericInput a) => a.Error;
    public static string FirstError(NumericInput a,NumericInput b) => a.HasError?a.Error:b.Error;
    public static string FirstError(NumericInput a,NumericInput b,NumericInput c) => a.HasError?a.Error:b.HasError?b.Error:c.Error;
    public static string FirstError(NumericInput a,NumericInput b,NumericInput c,NumericInput d) => a.HasError?a.Error:b.HasError?b.Error:c.HasError?c.Error:d.Error;
    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);
    private void Changed(string name) => PropertyChanged?.Invoke(this,name switch{
        nameof(Text)=>TextChanged,nameof(Value)=>ValueChanged,nameof(Error)=>ErrorChanged,nameof(HasError)=>HasErrorChanged,_=>new PropertyChangedEventArgs(name)});
}
