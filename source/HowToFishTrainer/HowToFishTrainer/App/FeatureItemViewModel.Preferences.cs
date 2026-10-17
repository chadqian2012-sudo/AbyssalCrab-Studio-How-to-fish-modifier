using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class FeatureItemViewModel
{
    private bool _restoringParameters;
    private ParameterValues? _pendingCatalogParameters;
    internal double LastValidSpinSpeed => _lastValidSpinSpeed;

    internal IReadOnlyDictionary<string, NumericInput> ParameterInputs => Descriptor.Control switch {
        FeatureControl.Number => new Dictionary<string, NumericInput> { ["value"] = ValueInput },
        FeatureControl.Generator => new Dictionary<string, NumericInput> {
            ["quantity"] = QuantityInput, ["distance"] = DistanceInput, ["weight"] = WeightInput },
        FeatureControl.Coordinates => new Dictionary<string, NumericInput> { ["x"] = XInput, ["y"] = YInput, ["z"] = ZInput },
        _ => new Dictionary<string, NumericInput>()
    };

    internal ParameterValues CaptureParameters()
    {
        var values = _pendingCatalogParameters?.Copy() ?? new ParameterValues();
        foreach (var pair in ParameterInputs) values.Put(pair.Key, pair.Value.LastValidValue);
        values.Put("saveAfter", SaveAfter);
        if (IsSpin) values.Put("spinSpeed", _lastValidSpinSpeed);
        if (Descriptor.Control == FeatureControl.Generator)
        {
            values.Put("search", SearchText); values.Put("kind", SelectedKind);
            values.Put("catalogView", CatalogView);
            values.Put("deadFish", DeadFish); values.Put("rare", Rare);
            if (SelectedOption != null)
            {
                values.Put("optionId", SelectedOption.Id);
                void Attachment(string key, string? id) { if (id != null || values.Text(key) == null) values.Put(key, id); }
                Attachment("sightId", SelectedSight?.Id); Attachment("barrelId", SelectedBarrel?.Id);
                Attachment("ammoId", SelectedAmmo?.Id); Attachment("skinId", SelectedSkin?.Id);
                values.Put("extendedMagazine", ExtendedMagazine); values.Put("laserSight", LaserSight);
            }
        }
        return values;
    }

    internal Dictionary<string, string> CaptureParameterDrafts()
    {
        var drafts = ParameterInputs.ToDictionary(p => p.Key, p => p.Value.Text, StringComparer.Ordinal);
        if (IsSpin) drafts["spinSpeed"] = SpinSpeedText;
        return drafts;
    }

    internal void RestoreParameters(ParameterValues values, IReadOnlyDictionary<string, string>? drafts = null)
    {
        _restoringParameters = true;
        try
        {
            if (Descriptor.Control == FeatureControl.Generator)
            {
                _pendingCatalogParameters = values.Copy();
                CatalogView = values.Text("catalogView") ?? "全部目录";
                SearchText = values.Text("search") ?? SearchText;
                string kind = values.Text("kind") ?? SelectedKind;
                SelectedKind = Options.Count == 0 || Kinds.Contains(kind) ? kind : "全部";
                if (values.Number("optionId") is double id && id == Math.Truncate(id))
                {
                    SelectedOption = Options.FirstOrDefault(o => o.Id == id);
                    if (SelectedOption != null && !FilteredOptions.Contains(SelectedOption)) SelectedOption = null;
                    if (Options.Count != 0 && SelectedOption == null)
                    { Status = "上次选择的物品或鱼类当前不可用，请重新选择"; StateLabel = "待选择"; }
                }
                else if (!values.ContainsKey("optionId")) SelectedOption = FilteredOptions.FirstOrDefault();
                SelectedSight = SelectedOption?.Sights.FirstOrDefault(o => o.Id == values.Text("sightId"));
                SelectedBarrel = SelectedOption?.Barrels.FirstOrDefault(o => o.Id == values.Text("barrelId"));
                SelectedAmmo = SelectedOption?.Ammo.FirstOrDefault(o => o.Id == values.Text("ammoId"));
                SelectedSkin = SelectedOption?.Skins.FirstOrDefault(o => o.Id == values.Text("skinId"));
                ExtendedMagazine = values.Boolean("extendedMagazine") ?? ExtendedMagazine;
                LaserSight = values.Boolean("laserSight") ?? LaserSight;
                DeadFish = values.Boolean("deadFish") ?? DeadFish;
                Rare = values.Boolean("rare") ?? Rare;
            }
            SaveAfter = values.Boolean("saveAfter") ?? SaveAfter;
            foreach (var pair in ParameterInputs)
            {
                if (values.Number(pair.Key) is double number && pair.Value.IsValidValue(number)) pair.Value.Value = number;
                if (drafts != null && drafts.TryGetValue(pair.Key, out var draft)) pair.Value.Text = draft;
            }
            if (IsSpin)
            {
                if (values.Number("spinSpeed") is double speed && speed >= 2 && speed <= 15)
                { _lastValidSpinSpeed = speed; SpinSpeedText = speed.ToString("G", CultureInfo.InvariantCulture); }
                if (drafts != null && drafts.TryGetValue("spinSpeed", out var draft)) SpinSpeedText = draft;
            }
        }
        finally { _restoringParameters = false; }
    }

    private void RememberAttachmentSelection(string key, string? id, string? previousId)
    {
        if (!_restoringParameters && _pendingCatalogParameters != null && (id != null || previousId != null))
            _pendingCatalogParameters.Put(key, id);
    }

    private bool HasUnresolvedCatalogChoice => _pendingCatalogParameters != null && (
        SelectedOption == null ||
        _pendingCatalogParameters.Text("sightId") is string sight && SelectedSight?.Id != sight ||
        _pendingCatalogParameters.Text("barrelId") is string barrel && SelectedBarrel?.Id != barrel ||
        _pendingCatalogParameters.Text("ammoId") is string ammo && SelectedAmmo?.Id != ammo ||
        _pendingCatalogParameters.Text("skinId") is string skin && SelectedSkin?.Id != skin);
}
