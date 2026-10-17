using System.Collections.Generic;

namespace HowToFishTrainer.App;

public static class FunEventDefinitions
{
	public const int MaxWaveEntities = 10;

	public const int MinimumSpawnGapMilliseconds = 120;

	public const int MinimumBirdGapMilliseconds = 180;

	public static IReadOnlyList<FunEventDefinition> All { get; } = new _003C_003Ez__ReadOnlyArray<FunEventDefinition>(new FunEventDefinition[14]
	{
		new FunEventDefinition("piranha-rain", "天降食人鱼", "来一波攻击性食人鱼，从视线前方上空落下。", FunEventRisk.Dangerous, 6),
		new FunEventDefinition("seagull-migration", "海鸥迁徙", "来一波海鸥横队，从房间玩家眼前飞过。", FunEventRisk.Lively, 8),
		new FunEventDefinition("fish-rain", "欢乐鱼雨", "来一波死亡普通鱼，热闹但不持续运行鱼类 AI。", FunEventRisk.Light, 8),
		new FunEventDefinition("crab-parade", "螃蟹横行", "来一波螃蟹，在前方排队横行。", FunEventRisk.Lively, 6),
		new FunEventDefinition("whale-fall", "鲸鱼落", "来一波大型死亡鱼，从上空缓缓落下。", FunEventRisk.Light, 1),
		new FunEventDefinition("rare-fish-school", "七彩极品鱼群", "来一波活体极品鱼，在前方扇形亮相。", FunEventRisk.Lively, 8),
		new FunEventDefinition("seafood-platter", "海鲜拼盘", "来一波死亡鱼类，围成一圈海鲜拼盘。", FunEventRisk.Light, 8),
		new FunEventDefinition("weapon-rain", "武器雨", "来一波随机武器，仅生成掉落物，不自动装备。", FunEventRisk.Light, 6),
		new FunEventDefinition("rod-forest", "钓竿森林", "来一波钓竿，在眼前围成小树林。", FunEventRisk.Light, 8),
		new FunEventDefinition("frisbee-storm", "飞盘风暴", "来一波飞盘，从玩家一侧掠向另一侧。", FunEventRisk.Light, 10),
		new FunEventDefinition("junk-bundle", "垃圾大礼包", "来一波普通杂物，不包含武器、鱼类和爆炸物。", FunEventRisk.Light, 8),
		new FunEventDefinition("seagull-feast", "海鸥开饭", "来一波小鱼与海鸥，组成临时开饭现场。", FunEventRisk.Lively, 7),
		new FunEventDefinition("boss-surprise", "Boss 惊喜盒", "来一波单只 Boss；已有 Boss 时会拒绝执行。", FunEventRisk.Dangerous, 1),
		new FunEventDefinition("ocean-parade", "海洋生物阅兵", "来一波混合非 Boss 生物，在前方列队经过。", FunEventRisk.Lively, 8)
	});
}
