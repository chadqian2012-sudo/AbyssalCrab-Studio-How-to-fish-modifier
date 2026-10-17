using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace HowToFishTrainer.App;

// The editable draft is never clamped, reformatted, or replaced while typing.
// Value keeps the last valid number; every submit path must check FirstError first.
public sealed class NumericInput : INotifyPropertyChanged
{
    private string _text;
    private double _value;
    private double _lastValidValue;
    public double LastValidValue => _lastValidValue;
    public string Label { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public bool IntegerOnly { get; }
    public string RangeDescription => Minimum.HasValue && Maximum.HasValue
        ? $"{Label}：{Format(Minimum.Value)}–{Format(Maximum.Value)}，{(IntegerOnly ? "需要整数" : "支持小数")}"
        : $"{Label}：{(IntegerOnly ? "需要整数" : "支持小数")}";
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;
            _text = value;
            if (TryParse(out double number, out _)) { _value = number; _lastValidValue = number; }
            Changed(nameof(Text)); Changed(nameof(Value)); Changed(nameof(Error)); Changed(nameof(HasError));
        }
    }
    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            if (IsValidValue(value)) _lastValidValue = value;
            Text = Format(value);
        }
    }
    public string Error { get { TryParse(out _, out string error); return error; } }
    public bool HasError => Error.Length != 0;
    public bool IsValidValue(double value) => double.IsFinite(value)
        && (!Minimum.HasValue || value >= Minimum.Value)
        && (!Maximum.HasValue || value <= Maximum.Value)
        && (!IntegerOnly || value == Math.Truncate(value));
    public event PropertyChangedEventHandler? PropertyChanged;
    public NumericInput(string label, double value, double? minimum, double? maximum, bool integerOnly = false)
    {
        Label = label; Minimum = minimum; Maximum = maximum; IntegerOnly = integerOnly;
        _value = value; _lastValidValue = value; _text = Format(value);
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
    public static string FirstError(params NumericInput[] inputs) => inputs.Select(input => input.Error).FirstOrDefault(error => error.Length != 0) ?? string.Empty;
    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
