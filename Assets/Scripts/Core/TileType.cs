namespace Omen.Core
{
    /// <summary>
    /// 地形类型。只描述"地面是什么"，不描述地面上的东西（草 / 生物见 <see cref="CellState"/>）。
    /// </summary>
    public enum TileType
    {
        /// <summary>海水：不可通行，生物无法进入，草也无法扩散。</summary>
        Water = 0,

        /// <summary>陆地：可通行，草可以扩散到这一格。</summary>
        Land = 1,
    }
}
