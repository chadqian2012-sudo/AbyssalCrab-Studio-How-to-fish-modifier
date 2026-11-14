using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed record SearchHit(string Title, string Category, string Id, string Description)
{
    public string PageTitle { get; init; } = Category;
}

public sealed partial class MainViewModel
{
    private bool _featureViewsDirty=true;
    private string? _featureViewCategory;
    private IReadOnlyList<FeatureItemViewModel> _feelView=Array.Empty<FeatureItemViewModel>();
    private IReadOnlyList<FeatureItemViewModel> _displayView=Array.Empty<FeatureItemViewModel>();
    private (SearchHit Hit, string Aliases, string Text)[]? _searchIndex;
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
        string query = FeatureQuery.Trim();
        if (query.Length == 0) return Array.Empty<SearchHit>();
        var culture = CultureInfo.CurrentCulture.CompareInfo;
        if (_cachedSearchQuery == query && ReferenceEquals(_cachedSearchCulture, culture)) return _cachedSearchResults;

        _searchIndex ??= BuildSearchIndex();
        const StringComparison comparison = StringComparison.CurrentCultureIgnoreCase;
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<(SearchHit Hit, int Rank)>();
        foreach (var entry in _searchIndex)
        {
            if (!terms.All(term => entry.Text.Contains(term, comparison))) continue;
            int rank = entry.Hit.Title.Equals(query, comparison) || entry.Hit.Id.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
                : entry.Hit.Title.StartsWith(query, comparison) ? 1
                : entry.Hit.Title.Contains(query, comparison) ? 2
                : entry.Aliases.Contains(query, comparison) ? 3 : 4;
            matches.Add((entry.Hit, rank));
        }
        _cachedSearchResults = matches.OrderBy(match => match.Rank).Select(match => match.Hit).ToArray();
        _cachedSearchQuery = query;
        _cachedSearchCulture = culture;
        return _cachedSearchResults;
    }

    private (SearchHit Hit, string Aliases, string Text)[] BuildSearchIndex()
    {
        var entries = new List<(SearchHit, string, string)>();
        void Add(string id, string title, string category, string description, string aliases = "")
        {
            var page = Pages.First(p => p.Category == category);
            entries.Add((new SearchHit(title, category, id, description) { PageTitle = page.Title }, aliases,
                string.Join(" ", title, id, category, page.Title, description, aliases)));
        }

        foreach (var feature in FeatureRegistry.All)
        {
            string aliases = feature.Id switch
            {
                "player.health.lock" => "锁血 无敌 血量 生命值 HP",
                "player.fullness.lock" => "锁饱食 饥饿 不饿",
                "player.move.multiplier" => "移速 跑速 步行速度",
                "player.sprint.multiplier" => "跑速 奔跑 冲刺速度",
                "player.jump.multiplier" => "跳高 跳跃力度",
                "player.spin.lock" => "旋转 转速 转圈 自动跳跃 收腿",
                "player.air_dash.lock" => "空中冲刺 快速冲刺 空中位移 Alt",
                "combat.aim_assist" => "自瞄 锁头 锁定队友 提前量 开镜",
                "combat.silent_aim" => "魔弹 子弹追踪 静默自瞄 锁定队友 追踪范围",
                "combat.recoil.zero" => "压枪 后坐力 后座力",
                "combat.ammo.lock" => "无限子弹 弹药",
                "combat.one_hit" => "秒杀 一击毙命",
                "generation.item" => "刷物品 造物 生成装备",
                "generation.fish" => "刷鱼 生成活鱼 生成死鱼",
                "world.island.load" => "岛屿传送 切换岛屿",
                _ => ""
            };
            Add(feature.Id, feature.Title, DisplayCategory(feature.Id, feature.Category), feature.Description, aliases);
        }

        Add("view.entity_overlay.configure", "实体标注", "战斗", "设置骨骼、红框及准心连线的显示距离。");
        Add("view.crosshair", "外置准星", "战斗", "调节准星形状、颜色和大小。", "准心 十字线");
        Add("combat.feedback", "连杀热潮与战斗反馈", "战斗", "设置命中标记、爆头提示音、伤害飘字、武器散布与连杀热度。", "击杀 击败提示音");
        Add("combat.diagnostics", "战斗诊断", "战斗", "查看当前武器与锁定状态。", "仅观察 诊断模式");
        Add("casino.roulette", "幸运轮盘控制台", "幸运娱乐", "设置轮盘结果模式、局数和赔率。", "轮盘赌");
        Add("fun.challenge.start", "海鸥飞靶挑战", "趣味小功能", "设置本轮海鸥数量并开始射击挑战。", "打靶 靶场");
        Add("fun.gull.auto_refill", "海鸥自动补充", "趣味小功能", "海鸥耗尽后自动补充，可设置波次上限。", "补鸟 自动补充海鸥");
        Add("fun.spin.curve", "大陀螺转速曲线", "趣味小功能", "选择固定、缓慢加速或脉冲旋转。");
        Add("players.catalog", "房间成员控制台", "房间成员", "查看在线成员，选择队友和作用范围。");
        Add("players.restore_room", "全房一键补满", "房间成员", "为房间成员恢复生命和饱食度。", "全房恢复");
        Add("world.position.last_death", "死亡前位置", "世界传送", "查看和使用上次记录的死亡位置。", "死亡坐标");
        Add("world.teleport.points", "保存的传送点", "世界传送", "保存、选择和管理坐标与分组。", "自定义传送点 坐标收藏");
        Add("journal.fishing", "钓鱼手记", "钓鱼", "浏览鱼获记录和每局回顾。", "日志 图鉴");
        Add("journal.backups", "存档备份与恢复", "解锁中心", "管理存档备份。", "还原存档 回档");
        foreach (var definition in FunEventDefinitions.All)
            Add("fun.event." + definition.Id, definition.Title, "趣味小功能", definition.Description,
                definition.HasAnimalCount ? "生成动物 数量" : "");
        return entries.ToArray();
    }

    private static bool HasDedicatedFeatureSection(string id) =>
        TeleportCustomFeatureIds.Contains(id) || CombatCustomFeatureIds.Contains(id) || id is "fishing.skin_jackpot" or "fun.slot_machine.rarity";
}
