# 《预兆》(OMEN)

> TapTap 聚光灯 GameJam 参赛作品 · 主题：**涌现**
> Unity 2022.3.62f2c1 · URP 2D · PC · 像素风 · 单人 10 天

观察一座小岛上的生态，押注哪个物种能活到最后，每 3 天选一个「预兆」影响全岛，
看系统自己走向你押注的结局——或者背道而驰。

---

## 现状

| Phase | 内容 | 状态 |
|---|---|---|
| 1 | 网格地图、相机、格子系统 | 🚧 进行中（1.1 数据层 ✅ / 1.2 视图层 🚧 / 1.3 时钟 ⬜） |
| 2 | 兔、狼、草行为逻辑 | ⬜ |
| 3 | 预兆系统、难度选择 | ⬜ |
| 4 | 押注系统、效果生效 | ⬜ |
| 5 | 胜负判定、结算界面 | ⬜ |
| 6 | 丰富预兆系统 | ⬜ |
| 7 | 像素美术、动画、音效 | ⬜ |
| 8 | 难度平衡、测试 | ⬜ |
| 9 | 修 bug、打磨、录屏 | ⬜ |
| 10 | 打包、提交 | ⬜ |

完整设计见 [Docs/Design.md](Docs/Design.md)。

---

## 目录结构

```
Assets/
  Scenes/            场景
  Scripts/
    Core/            纯 C# 数据与规则，零 UnityEngine 依赖 ← 唯一真相来源
    Config/          ScriptableObject 配置资产
    Systems/         按 tick 驱动的逻辑系统
    View/            把 Core 状态投影成画面
    Debug/           HUD、调试工具
    Bootstrap/       场景入口，只负责装配
Docs/
  Design.md          设计文档 + 架构契约
```

## 架构铁律

1. **`IslandGrid` 是唯一真相**：任何系统改状态必须走它的方法，禁止绕过它直接改画面
2. **`View` 只读**：View 层不产生玩法状态，只做投影
3. **`Core` 不引用 UnityEngine**：保证 JSON 存档、回放、单元测试全部可行
4. **`WorldGrid` 是坐标转换的唯一入口**
5. **不引入第三方框架**（Zenject / UniRx / Entitas 等）

---

## 如何运行

1. 用 **Unity 2022.3.x** 打开本项目
2. 打开场景 `Assets/Scenes/SampleScene.unity`
3. 确认场景里有一个空物体挂着 `GameBootstrap` 组件
4. 按 **Play**，Console 会打印 ASCII 岛屿地图

## 岛屿生成参数

在 `Assets/Config/IslandConfig.asset`（`Create → Omen → Island Config`）里调：

| 参数 | 说明 |
|---|---|
| `ShapeLobes` | 半岛/海湾数量，4 = 经典小岛 |
| `ShapeRadiusJitter` | 半岛长度落差，越大越夸张 |
| `ShapeInfluence` | 海岸线起伏幅度（乘性遮罩，随岛半径等比缩放） |
| `CoastNoiseInfluence` | 海岸线小尺度锯齿强度 |
| `LandRatio` | 目标陆地占比，算法自动逼近 |
| `Seed` | 0 = 每次随机；非 0 = 固定可复现 |
