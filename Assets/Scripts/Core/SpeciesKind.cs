namespace Omen.Core
{
    /// <summary>
    /// 物种种类。
    ///
    /// 注意它只描述"是什么生物"，不描述"怎么行动" —— 行为参数全部在
    /// <see cref="SpeciesConfigData"/> 里，代码里不出现 if (兔子) 这种硬编码分支。
    /// 这样 Phase 6 加新预兆、甚至加第四个物种时都不用改模拟逻辑。
    /// </summary>
    public enum SpeciesKind
    {
        /// <summary>没有物种。仅用于占位，不应出现在活着的生物身上。</summary>
        None = 0,

        /// <summary>兔：吃草、躲狼、定期繁殖。</summary>
        Rabbit = 1,

        /// <summary>狼：追兔、进食恢复能量、饿久会死、定期繁殖。</summary>
        Wolf = 2,

        /// <summary>草：向相邻陆地扩散，被兔吃掉。</summary>
        Grass = 3,
    }

    /// <summary>物种枚举的辅助方法。</summary>
    public static class SpeciesKindExtensions
    {
        /// <summary>是不是会移动、会饿死的动物（对应 <see cref="Creature"/> 里的能量字段）。</summary>
        public static bool IsAnimal(this SpeciesKind kind)
        {
            return kind == SpeciesKind.Rabbit || kind == SpeciesKind.Wolf;
        }

        /// <summary>是不是植物（长在格子上，不占生物位）。</summary>
        public static bool IsPlant(this SpeciesKind kind)
        {
            return kind == SpeciesKind.Grass;
        }
    }
}
