using System;
using System.Collections.Generic;
using System.Globalization;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed partial class MainViewModel
{
    private bool _featureViewsDirty=true;
    private string? _featureViewCategory;
    private IReadOnlyList<FeatureItemViewModel> _feelView=Array.Empty<FeatureItemViewModel>();
    private IReadOnlyList<FeatureItemViewModel> _displayView=Array.Empty<FeatureItemViewModel>();
    private SearchHit[]? _searchIndex;
    private string[]? _searchTextIndex;
    private string? _cachedSearchQuery;
    private CompareInfo? _cachedSearchCulture;
    private IReadOnlyList<SearchHit> _cachedSearchResults=Array.Empty<SearchHit>();

    private void InitializePerformanceCaches()
    { Features.CollectionChanged+=(_,_)=>_featureViewsDirty=true; }

    private void EnsureFeatureViews()
    {
        string category=SelectedPage.Category;
        if(!_featureViewsDirty&&_featureViewCategory==category)return;
        bool hasFeel=category is "玩家" or "钓鱼";
        var feel=new List<FeatureItemViewModel>();var display=new List<FeatureItemViewModel>();
        // Except() previously also deduplicated the displayed collection; retain that behavior.
        var seen=new HashSet<FeatureItemViewModel>();
        foreach(var feature in Features)
        {
            if(hasFeel&&feature.Descriptor.Control==FeatureControl.Number)feel.Add(feature);
            else if(seen.Add(feature))display.Add(feature);
        }
        _feelView=feel.Count==0?Array.Empty<FeatureItemViewModel>():feel.AsReadOnly();
        _displayView=display.Count==0?Array.Empty<FeatureItemViewModel>():display.AsReadOnly();
        _featureViewCategory=category;_featureViewsDirty=false;
    }
    private IReadOnlyList<SearchHit> GetSearchResults()
    {
        if(string.IsNullOrWhiteSpace(FeatureQuery))return Array.Empty<SearchHit>();
        var culture=CultureInfo.CurrentCulture.CompareInfo;
        if(_cachedSearchQuery==FeatureQuery&&ReferenceEquals(_cachedSearchCulture,culture))return _cachedSearchResults;
        if(_searchIndex==null)
        {
            var hits=new List<SearchHit>();var texts=new List<string>();
            foreach(var feature in FeatureRegistry.All)
            {
                string category=DisplayCategory(feature.Id,feature.Category);
                hits.Add(new(feature.Title,category,feature.Id,feature.Description));
                texts.Add(feature.Title+" "+feature.Description+" "+feature.Id+" "+category);
            }
            _searchIndex=hits.ToArray();_searchTextIndex=texts.ToArray();
        }
        string query=FeatureQuery.Trim();var matches=new List<SearchHit>();
        for(int i=0;i<_searchIndex.Length&&matches.Count<55;i++)
            if(_searchTextIndex![i].Contains(query,StringComparison.CurrentCultureIgnoreCase))matches.Add(_searchIndex[i]);
        _cachedSearchResults=matches.AsReadOnly();_cachedSearchQuery=FeatureQuery;_cachedSearchCulture=culture;
        return _cachedSearchResults;
    }
}
