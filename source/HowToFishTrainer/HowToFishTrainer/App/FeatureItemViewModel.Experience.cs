using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class FeatureItemViewModel
{
    private ExperienceSettings? _experience;
    private Action? _saveExperience;
    private Action<FeatureItemViewModel,ParameterValues>? _generated;
    private Func<string?>? _executionBlockReason;
    private string _catalogView = "全部目录";
    private string? _selectedRecentSearch;
    public string? SelectedRecentSearch { get=>_selectedRecentSearch;set{_selectedRecentSearch=value;Changed();if(value!=null)SearchText=value;} }
    public string[] CatalogViews { get; } = { "全部目录", "收藏", "最近生成" };
    public string CatalogView
    {
        get => _catalogView;
        set { if (_catalogView == value || !CatalogViews.Contains(value)) return; _catalogView = value; Changed(); Changed(nameof(FilteredOptions)); SelectFirstFiltered(); }
    }
    public bool IsFavorite => SelectedOption != null && _experience?.Favorites.Any(e => e.FeatureId == Descriptor.Id && e.Id == SelectedOption.Id) == true;
    public string FavoriteButtonText => IsFavorite ? "★ 已收藏（点击取消）" : "☆ 收藏当前物品与配置";
    public IReadOnlyList<string> RecentSearches => _experience?.Searches.ToArray() ?? Array.Empty<string>();
    public ICommand ToggleFavoriteCommand => new RelayCommand(() => {
        if (SelectedOption == null || _experience == null) return;
        var existing = _experience.Favorites.FirstOrDefault(e => e.FeatureId == Descriptor.Id && e.Id == SelectedOption.Id);
        if (existing == null) _experience.Favorites.Insert(0, new(Descriptor.Id, SelectedOption.Id, SelectedOption.Name, CaptureParameters()));
        else _experience.Favorites.Remove(existing);
        _saveExperience?.Invoke(); NotifyCatalogExperience();
    });
    public ICommand RememberSearchCommand => new RelayCommand(() => {
        string query = SearchText.Trim(); if (query.Length == 0 || query.Length > 100 || _experience == null) return;
        _experience.Searches.Remove(query); _experience.Searches.Insert(0,query);
        if (_experience.Searches.Count > 12) _experience.Searches.RemoveRange(12,_experience.Searches.Count-12);
        _saveExperience?.Invoke(); Changed(nameof(RecentSearches));
    });
    public ICommand RestoreBookmarkedConfigurationCommand => new RelayCommand(() => {
        if (SelectedOption == null || _experience == null) return;
        var entry = (CatalogView == "最近生成" ? _experience.Recent : _experience.Favorites).FirstOrDefault(e => e.FeatureId == Descriptor.Id && e.Id == SelectedOption.Id);
        if (entry == null) { Status = "当前物品没有已保存的配置"; return; }
        // Keep the current filter/view; a bookmark restores compatible IDs and values only.
        var parameters = entry.Parameters.Copy(); parameters.Put("search",SearchText); parameters.Put("kind",SelectedKind);
        RestoreParameters(parameters); Status = "已恢复收藏/最近生成配置，点击生成后才会执行";
    });
    internal void AttachExperience(ExperienceSettings settings, Action save, Action<FeatureItemViewModel,ParameterValues> generated, Func<string?>? blockReason = null)
    { _experience=settings; _saveExperience=save; _generated=generated; _executionBlockReason=blockReason; NotifyCatalogExperience(); }
    internal void RefreshExperienceAvailability() => Changed(nameof(CanInteract));
    private bool MatchesCatalogView(CatalogOption option) => CatalogView switch {
        "收藏" => _experience?.Favorites.Any(e => e.FeatureId == Descriptor.Id && e.Id == option.Id) == true,
        "最近生成" => _experience?.Recent.Any(e => e.FeatureId == Descriptor.Id && e.Id == option.Id) == true,
        _ => true
    };
    private IEnumerable<CatalogOption> SortCatalogOptions(IEnumerable<CatalogOption> source) => CatalogView == "最近生成" && _experience != null
        ? source.OrderBy(o => _experience.Recent.FindIndex(e => e.FeatureId == Descriptor.Id && e.Id == o.Id)) : source;
    private void NotifyCatalogExperience() { Changed(nameof(FilteredOptions)); Changed(nameof(IsFavorite)); Changed(nameof(FavoriteButtonText)); Changed(nameof(RecentSearches)); }
    public ICommand ResetParametersCommand => new RelayCommand(() => {
        var defaults = new FeatureItemViewModel(Descriptor);
        foreach (var input in ParameterInputs) input.Value.Value = defaults.ParameterInputs[input.Key].LastValidValue;
        if (IsSpin) SpinSpeedText = "4";
        Status = "已恢复输入默认值；点击应用后才会写入游戏";
    });
}
