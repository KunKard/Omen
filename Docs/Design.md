# 《预兆》设计文档

> TapTap 聚光灯 GameJam 参赛作品 · 主题：**涌现**
> 单人开发 · 10 天周期 · Unity 2022.3.62f2c1 · URP 2D · PC · 像素风

---

## 0. 文档说明

| 项 | 内容 |
|---|---|
| 本文件用途 | 设计定稿 + 架构契约。所有设计决策以本文件为准 |
| 维护方式 | 每个 Phase 完成后追加对应章节；开发日志另行记录（`Docs/DevLog.md`） |
| 最后更新 | Phase 1 规划定稿 |

---

## 1. 一句话概念

> 观察一座小岛上的生态，押注哪个物种能活到最后，每 3 天选一个「预兆」影响全岛，看系统自己走向你押注的结局——或者背道而驰。

**玩家不是操盘手，而是观察者 + 押注者。** 每一次干预都改变全局规则，但结果由系统自己涌现，玩家无法完全控制。这是主题「涌现」的核心表达方式。

---

## 2. 设计支柱

1. **可读**：任何时候玩家都能一眼看懂岛上正在发生什么（谁在吃谁、草长到哪了）
2. **可解释**：结局必须能回溯到具体转折点（哪一天的哪个预兆改变了走向）
3. **不可控**：存在混沌与运气，玩家的最优解不是"背板"，而是"读局 + 对冲"
4. **短局**：单局约 3 分钟，失败也想再来一把

### 2.1 已完成的设计决策（Decision Log）

| # | 决策 | 理由 | 日期 |
|---|---|---|---|
| D1 | 架构采用**分层「类 ECS」**，不用 DOTS/Entitas | 纯 ECS 在 10 天单人周期里编译慢、调试难、与 uGUI 联动繁琐；16×16 网格最多几百实体，性能非瓶颈 | Phase 1 |
| D2 | 地图渲染用**对象池 Tile GameObject**，不用 Tilemap | 草会动态生长/被吃，Tilemap 会导致「格子状态」与「Tilemap 状态」两份真相互相打架 | Phase 1 |
| D3 | 像素完美对齐**手写**，不装 `com.unity.2d.pixel-perfect` | 当前 Packages 未包含该包；`orthographicSize = 屏幕像素高 / (2 × PPU)` 一行搞定 | Phase 1 |
| D4 | 时间尺度：**1 天 = 12 秒**，1 tick = 1 秒 | 一局 15 天 ≈ 180 秒，契合评审试玩耐心 | Phase 1 |
| D5 | 像素基准：**PPU = 16**，1 格 = 1 世界单位 | 像素风惯例；后续所有美术资源按此切图 | Phase 1 |
| D6 | 岛屿形状：**不规则海岸线**（距离场 + 纯 C# 值噪声扰动） | 有海湾/半岛，视觉更像"岛"，且有天然地形分区，生态更有戏 | Phase 1 |
| D7 | `Core/` 层纯 C#，**不引用 UnityEngine** | 保证 JSON 存档、回放、单元测试、网格快照全部可行 | Phase 1 |
| D8 | 屏幕缩放 = `PixelsPerUnit`（美术采图标准，固定 16，长期不动）× `Zoom`（**整数**放大倍数，默认 2） | 非整数倍放大时相邻像素会占 1 个或 2 个屏幕像素，像素大小不一很显眼。拆成两个参数后，美术标准与屏幕缩放各管各的，调大画面不会污染美术契约 | Phase 1.2 |
| D9 | 岛屿尺寸**不随难度变化**，保持 16×16 | 16×16 有 256 格，生态变量已经够多；更大的网格会拉长"观察—反馈"节奏，与 3 分钟一局的短局目标冲突。**待用户实测后复议** | Phase 1.2 |

---

## 3. 核心循环

```
启动 → 难度选择 → 观察 3 天 → [押注物种] → 选 1 个预兆
                                    ↓
              Day 3 / 6 / 9 / 12 各重复一次（押注 + 预兆）
                                    ↓
                            Day 15 结算：押注物种是否存活
```

时间轴：

| 时刻 | 事件 |
|---|---|
| Day 1–3 | 观察期（玩家不能干预，只能看） |
| Day 3 | 押注 + 选预兆 ① |
| Day 6 | 押注 + 选预兆 ② |
| Day 9 | 押注 + 选预兆 ③ |
| Day 12 | 押注 + 选预兆 ④ |
| Day 15 | **结算**：判定押注物种存活 → 胜负界面 |

> 一局总时长：15 天 × 12 秒 = **180 秒**（3 分钟）。

---

## 4. 地图

| 项 | 规格 |
|---|---|
| 尺寸 | 16 × 16 格，共 256 格 |
| 坐标 | `(0,0)` 在左下角，`(15,15)` 在右上角；世界坐标同向，1 格 = 1 世界单位 |
| 地形 | `Water`（海水，不可通行）/ `Land`（陆地，可通行） |
| 边缘规则 | 最外圈强制为 `Water`，保证岛屿被海包围、生物不会走出去 |
| 格子附加状态 | `hasGrass`（是否有草）、`creatureId`（占据者，-1 表示空） |
| 生成 | 确定性：同一种子 + 同一参数 ⇒ 完全相同的岛（用于复现 bug、回放、日志） |
| 配色 | 海水 `#2E5C8A` / 陆地 `#6B8E4E` / 草地 `#8FBF4A`（Phase 1 占位色） |

### 4.1 岛屿生成算法（Phase 1 实现）

1. **径向距离场**：`d = 到中心的距离 / 岛屿半径`，得到基础团块
2. **噪声扰动**：`n = valueNoise(x, y, seed)`，采样 2–3 个倍频程（octave），得到平滑海岸线
3. **阈值判定**：`d + n * 扰动强度 < 陆地阈值` ⇒ `Land`，否则 `Water`
4. **强制海边界**：最外圈设为 `Water`
5. **连通性保证**：若陆地不连通，只保留最大连通块（其余填回海水），避免生物被困死在孤岛

所有噪声使用**纯 C# 整数哈希值噪声**（不依赖 `Mathf.PerlinNoise`），保证跨平台/跨版本确定性。

---

## 5. 物种（3 个）

| 物种 | 行为规则 | 存活条件 |
|---|---|---|
| **兔** | 随机向相邻空格移动；遇到草则吃（清除该格草）；遇到狼则朝反方向逃；能量耗尽前定期繁殖 | Day 15 时岛上仍有 ≥ 1 只 |
| **狼** | 朝最近的兔移动；吃到兔则恢复体力；长期饥饿则死亡；体力充足时定期繁殖 | Day 15 时岛上仍有 ≥ 1 只 |
| **草** | 每回合向相邻空陆地格扩散；被兔吃掉则消失 | 覆盖 **≥ 50% 陆地格** |

设计约束：每个物种的行为必须**一句话说清**，玩家不需要教程。

---

## 6. 预兆系统

- 触发时机：**Day 3 / 6 / 9 / 12**（Day 15 进入结算，不再给）
- 每次弹出 **3 个候选**，玩家选 **1 个**
- 每个预兆**有利有弊**，且**影响所有物种**（不是只加强某一方）
- 实现方式：**ScriptableObject 数据驱动**，每个预兆挂一个效果函数
- 设计目标：让"选预兆"成为改变全局规则的操作，而非简单的数值加成

---

## 7. 押注系统

- 首次押注在 **Day 3**（先选预兆，再押注）
- 之后 Day 6 / 9 / 12 可重复押注，**难度递增**

| 难度 | 押注选项 | 观察期 | 岛屿尺寸 |
|---|---|---|---|
| 1 | 兔 / 狼 | 3 天 | 16 × 16 |
| 2 | 兔 / 狼 / 草 | 3 天 | 待定 |

### 7.1 岛屿尺寸随难度递增（待实现，Phase 3 落地）

**用户决策（Phase 1.2）**：不做成默认，**留给高难度**。

实现要点（Phase 3 做难度选择界面时落地）：

- `IslandConfig` 的 `width` / `height` 目前是单一资产上的固定值，需要改为"按难度取表"
- 建议形式：在 `IslandConfig` 上加一个 `IslandSizeByDifficulty` 数组（索引 = 难度），
  难度选择后由 `GameBootstrap` 覆写尺寸再生成
- **注意**：尺寸变化会连带影响三件事，Phase 3 要一起处理
  1. `CameraRig.Configure()` 会自动重新构图，但 `Zoom` 需要按尺寸重算，
     否则大网格会溢出屏幕（16 格放得下 zoom=2，32 格就放不下了）
  2. 岛屿生成时间随格数线性增长（当前 16×16 约 1 ms，可忽略；64×64 需要实测）
  3. 草覆盖率 ≥50% 的判定基准是**陆地格数**而非总格数，逻辑上已经是这样，无需改

---

## 8. 结算

- **Day 15** 检查：玩家最后押注的物种是否满足其存活条件
- 输出：胜负结果 + **转折点回溯**（展示本局关键事件时间线）

---

## 9. 技术栈

| 模块 | 方案 | 状态 |
|---|---|---|
| 引擎 | Unity 2022.3.62f2c1 | ✅ 已确认 |
| 渲染 | URP 14.0.12 + Renderer2D | ✅ 已配置 |
| 地图 | 16 × 16 二维数组（`CellState[,]`） | Phase 1 |
| 生物 | 对象池 + 简单状态机 | Phase 2 |
| 回合 | 半实时：1 天 = 12 秒，1 tick = 1 秒 | Phase 1 |
| 预兆 | 数据驱动，ScriptableObject | Phase 3 |
| 押注 | uGUI + 状态变量 | Phase 4 |
| 结算 | 物种存活检查 | Phase 5 |
| 存档 | JSON | Phase 5 |
| 架构 | 分层「类 ECS」：纯 C# 数据层 + View 层 + System 层 | Phase 1 |

---

## 10. 架构契约

### 10.1 分层职责

```
Bootstrap/   场景入口，只负责「装配」，不含玩法逻辑
    ↓
Systems/     按 Tick 驱动的逻辑系统（时钟、生物行为、预兆）
    ↓
Core/        纯 C# 数据与规则，零 UnityEngine 依赖 ← 唯一真相
    ↑
View/        把 Core 的状态投影成画面（SpriteRenderer 池），单向读取
Debug/       HUD / 调试面板，只读不写
```

### 10.2 铁律

1. **`IslandGrid` 是唯一真相**：任何系统改状态必须走 `IslandGrid` 的方法，禁止绕过它直接改颜色/坐标/Tilemap
2. **`View` 只读**：View 层不产生玩法状态，只做投影；View 不得被玩法逻辑反向查询
3. **`Core` 不引用 UnityEngine**：保证 JSON 序列化、回放、单元测试、快照对比全部可行
4. **`WorldGrid` 是坐标转换的唯一入口**：`GridPos ↔ 世界坐标` 只允许在这一处实现
5. **不引入第三方框架**：不装 Zenject / VContainer / UniRx / Entitas 等，10 天周期内自建最省心

> 铁律 1 的直接收益：Phase 5 的「转折点回溯」只需记录 grid 快照序列即可实现。

### 10.3 目录结构

```
Assets/
  Scenes/
    Main.unity                  场景内仅 1 个空物体挂 GameBootstrap
  Scripts/
    Core/                       纯 C#（不引用 UnityEngine）
      GridPos.cs                struct 坐标，IEquatable，GetHashCode 无 GC
      TileType.cs               enum { Water, Land }
      CellState.cs              struct { TileType type; bool hasGrass; int creatureId; }
      IslandGrid.cs             16×16 数据层：索引 / 邻居 / 统计 / 变更事件
      IslandGenerator.cs        确定性岛屿生成
      ValueNoise.cs             纯 C# 整数哈希值噪声
    Config/
      IslandConfig.cs           ScriptableObject：宽高 / 种子 / 陆地比例 / 颜色 / PPU
      GameClockConfig.cs        ScriptableObject：天数 / 秒每天 / 秒每 tick
    Systems/
      GameClock.cs              半实时时钟，暴露 Day / Tick 事件
    View/
      WorldGrid.cs              GridPos ↔ 世界坐标（唯一入口）
      TileView.cs               单格视觉
      GridView.cs               订阅事件 → 驱动 TileView 池
      CameraRig.cs              构图 + 像素对齐
    Debug/
      DebugHud.cs               OnGUI 屏上 HUD（零依赖）
    Bootstrap/
      GameBootstrap.cs          场景唯一入口
```

---

## 11. 开发排期

| Phase | 任务 | 状态 |
|---|---|---|
| 1 | 网格地图、相机、格子系统 | 🚧 进行中 |
| 2 | 兔、狼、草行为逻辑 | ⬜ |
| 3 | 预兆系统、难度选择 | ⬜ |
| 4 | 押注系统、效果生效 | ⬜ |
| 5 | 胜负判定、结算界面、玩法最小循环 | ⬜ |
| 6 | 丰富预兆系统 | ⬜ |
| 7 | 打磨像素美术、动画、音效 | ⬜ |
| 8 | 难度平衡、测试 | ⬜ |
| 9 | 修 bug、打磨、录屏 | ⬜ |
| 10 | 打包、提交 | ⬜ |

---

## 12. Phase 1 详规

### 12.1 目标

> 打开 Unity 按 Play，能看到一座清晰的 16×16 小岛，帧率稳定，且有调试 HUD。

### 12.2 验收清单

- [ ] 16×16 岛屿：外围海水、内部陆地、海岸线不规则（带海湾/半岛）
- [ ] 同种子必然长出同一座岛；不同种子长出不同的岛
- [ ] 陆地 / 海水 / 草 三种颜色肉眼可分
- [ ] 相机居中且完整框住岛屿，四周留边，不裁切
- [ ] HUD 显示 `FPS / Day / Tick / Land / Grass`
- [ ] 按 `R` 同种子重建；按 `N` 随机种子重建
- [ ] Console 零报错零警告；运行时无每帧 GC 分配
- [ ] `Core/` 下不出现 `using UnityEngine`

### 12.3 明确不做

兔/狼/草行为逻辑、预兆、押注、UI 界面、回合推进、动画、音效、美术资源。

### 12.4 执行顺序

| 步骤 | 内容 | 验收方式 | 状态 |
|---|---|---|---|
| 1.1 | `GridPos / TileType / CellState / GridUtility / IslandGrid / ValueNoise / IslandGenerator / IslandConfigData / IslandConfig / GameBootstrap` | `GameBootstrap` 打印 ASCII 地图（`~`=水，`.`=陆），Console 肉眼确认形状 | ✅ 已验收 |
| 1.2 | `WorldGrid / RuntimeSprite / CellPalette / TileView / GridView / CameraRig` | Play 看到彩色岛，相机居中留边，画面不糊 | ✅ 已验收（Camera Zoom = 3） |
| 1.3 | `GameClockData / GameClockConfig / GameClock / DebugHud` + 热键 | Day 每 12 秒 +1，Tick 每秒 +1，HUD 实时刷新，热键重建岛 | 🚧 待验收 |

### 12.10 Phase 1.3 在 Unity 里的验收步骤

**不需要手动操作**，`GameBootstrap` 会自动创建 `GameClock` 与 `DebugHud`。

1. 切到 Unity，等编译完
2. 按 **Play**
3. 左上角应出现半透明黑底调试面板

**验收要点**

- [ ] HUD 显示 `FPS / 屏幕 / Day 1 / 15 / Tick 0 / 12 / [运行中] / 距离下一天 x.x 秒 / Day 进度`
- [ ] `Tick` 每 1 秒 +1，到 12 后归零同时 `Day` +1
- [ ] `Day 进度` 是百分比，随时间增长
- [ ] HUD 显示 `相机 正交尺寸 / PPU / Zoom`，**PPU 应等于 16 × 你设置的 Camera Zoom**
      （如果你看到 PPU 比预期小，说明屏幕不够大被自动降档了 —— 这正是要监控的指标）
- [ ] 按 **空格** → 状态变 `[已暂停]`，时间停住；再按恢复
- [ ] 按 **R** → 用同一种子重建岛屿，画面完全一致，且 **Day 回到 1**
- [ ] 按 **N** → 换新种子，岛形变化，Day 回到 1
- [ ] 按 **F1** → HUD 隐藏 / 显示
- [ ] Console 零报错

### 12.11 热键一览（`GameBootstrap.enableHotkeys`，发布时关掉）

| 键 | 作用 |
|---|---|
| `R` | 用**同一**种子重建（验证确定性；Day 归 1） |
| `N` | 换**新**种子重建（换一张岛；Day 归 1） |
| `空格` | 暂停 / 继续时钟 |
| `F1` | 显示 / 隐藏 HUD |

### 12.12 时钟的边界语义（已用单测钉死）

时间约定：**Day 从 1 开始；Tick 是当天内的序号，从 0 到 `TicksPerDay-1`**。

| 事件 | 语义 |
|---|---|
| 第 1 天的 Tick | 1、2、…、12（TickInDay 依次为 0…11，注意第 1 个 tick 发生在第 1 秒末） |
| t = 12.0 秒 | 第 12 个 tick 发生的**同时**跨天 → `Day=2`、`TickInDay=0`、进度归零 |
| 最后一个 tick | Day 15 的第 12 个 tick 仍会触发，之后才 `Finished`（不会吞掉最后一个 tick） |
| 一局总量 | 15 天 × 12 tick = **180 个 tick**，180 秒 |
| 单帧补帧上限 | 8 个 tick。编辑器暂停后恢复不会"追帧雪崩"（超出部分直接丢弃） |
| `TimeScale` | 预兆要"全岛加速"时改这一个值即可，不会丢 tick 或重复 tick |

### 12.6 Phase 1.1 在 Unity 里的验收步骤（已完成，保留备查）

1. 用 **Unity 2022.3.x** 打开本项目（首次打开会重新导入 Library，需要几分钟）
2. 打开场景 `Assets/Scenes/SampleScene.unity`
3. 菜单 **GameObject → Create Empty**，命名为 `GameBootstrap`
4. 在 Inspector 里 **Add Component** → 搜 `GameBootstrap` → 添加
5. （可选，推荐）在 Project 窗口右键 **Create → Omen → Island Config**，
   放到 `Assets/Config/IslandConfig.asset`（`Config` 文件夹需新建），
   然后把这个资产拖到 `GameBootstrap` 的 `Island Config` 字段上
6. （可选）选中 `Main Camera`，把 **Orthographic Size** 从 5 改成 **9**，
   这样 16×16 的岛（宽 16 单位）才装得下 —— 否则 Phase 1.2 会看到被裁切的岛
7. 保存场景（Ctrl+S），按 **Play**
8. Console 应输出 ASCII 岛屿地图 + 统计行

**验收要点**

- [ ] Console 里岛的外圈全是 `~`，内部是 `.`，形状不规则（不是正圆）
- [ ] 统计行里"陆地格数"在 **108 ~ 118** 之间（目标占比 45%）
- [ ] "孤岛裁掉" 一般为 0；偶尔大于 0 也正常（那是噪声产生的零碎小岛被清理）
- [ ] 同一种子再次 Play，ASCII 图**逐字符一致**
- [ ] 改 `Island Config` 的 `Seed` 为非 0 值，图应固定不变；改为 0 则每次不同
- [ ] Console **零报错零警告**

**已知的中间状态**：此刻按 Play 场景里**还看不到任何画面**（Phase 1.1 只做数据层）。
如果 Unity 在 Play 时因为场景里只有相机而提示"没有物体"，属正常。

### 12.7 岛屿生成参数的调参手感（Phase 1.1 实测结论）

| 参数 | 效果 | 推荐区间 |
|---|---|---|
| `ShapeLobes` | 半岛/海湾数量 | 4（经典小岛）；3 = 三叶形；8 = 高度破碎 |
| `ShapeRadiusJitter` | 半岛长度落差 | 0.5~0.6（0.55 时最长/最短方向差半个岛半径） |
| `ShapeInfluence` | 海岸线起伏幅度 | 0.35~0.45；上限 0.5（超过会把陆地撕成两块） |
| `CoastNoiseInfluence` | 海岸线小尺度锯齿 | 0.15~0.25；0 = 轮廓平滑如几何图形 |
| `LandRatio` | 陆地占比 | 0.40~0.55，实测二分搜索误差 < 1% |

**踩过的坑（写下来避免以后重犯）**：最初把形状写成加法项 `d - shape * influence`，
结果噪声与形状的梯度都被径向距离场的梯度（约 1.0）淹没，500 个种子生成的都是**正圆**。
改成乘性遮罩 `d / mask` 后，轮廓偏移量随岛半径等比缩放，半岛与海湾才真正显现。
**结论：在距离场上做形状扰动，必须用乘法而不是加法。**

### 12.8 Phase 1.2 在 Unity 里的验收步骤

**不需要任何手动操作** —— `GameBootstrap` 会在 `Awake` 里自动创建 `GridView` 并给相机挂上 `CameraRig`。

1. 切到 Unity 窗口，等它编译完（右下角转圈结束）
2. 直接按 **Play**
3. 应该看到：深蓝色海水包围一块不规则的绿色陆地，整体居中，四周有留白

**验收要点**

- [ ] 岛是**绿色**、海水是**深蓝色**，两种颜色对比明显
- [ ] 岛**完整在画面内**，没有贴着屏幕边缘或被裁掉
- [ ] 岛**居中**，上下左右留白大致对称
- [ ] 方块边缘**锐利不发虚**（像素对齐生效；如果糊了告诉我，是 `CameraRig` 的整数对齐没生效）
- [ ] Console 依然是那套 ASCII 地图 + 统计，**没有新的报错或警告**
- [ ] Hierarchy 里出现 `GameBootstrap/GridView/Tile_0_0` … `Tile_15_15` 共 256 个物体

**如果岛看起来偏小**：把 `IslandConfig` 资产的 **Camera Zoom** 调大。

> 关键：`CameraRig` 与 `GridView` 都是 `GameBootstrap` 在**运行时**挂上去的组件
> （不写回场景），所以你在 Inspector 里看不到 `CameraRig` 的字段。
> 为了让你有地方调，缩放参数走的是**配置资产**这条路：
> `Assets/Config/IslandConfig.asset → 显示 → Camera Zoom`。

| Camera Zoom | 1080p 下 1 格占屏幕像素 | 岛占屏幕高度 | 说明 |
|---|---|---|---|
| 1 | 16 | ≈ 24% | 原始大小，太小 |
| **2（默认）** | 32 | **≈ 47%** | 推荐起点 |
| 3 | 48 | ≈ 71% | 再大就会顶到上下边缘 |
| 4 | 64 | ≈ 95% | 1080p 下的极限，几乎贴边 |

> 为什么只能用整数 Zoom：若为 1.5 倍，48 个美术像素要摊到 72 个屏幕像素上，
> 平均每像素 1.5 格 —— 相邻像素会一个占 1 像素、一个占 2 像素，看起来像间隔条纹。
> 整数倍则每个美术像素都放大同样多，画面干净。

屏幕太小装不下时会**自动降档**（例如 720p 下 zoom=3 会被压到约 2.4 倍），不会把岛裁掉。
实际生效值可在运行时通过 `CameraRig.EffectivePixelsPerUnit` 读出，Phase 1.3 的 HUD 会显示它。

改 `IslandConfig` 的 **Width / Height** 也能让岛变大，但那是**模拟规模**变化而不是画面缩放 ——
适合留给高难度（见 §7.1）。

**Phase 1.2 新增的架构机制：脏格子追踪**

`IslandGrid` 现在维护一份"自上次同步以来变过的格子"列表：

| 机制 | 用途 |
|---|---|
| `DirtyCount` / `GetDirtyIndex(i)` | View 层只遍历变了的格子，不必每帧扫 256 格 |
| `IsDirty(index)` | O(1) 查询单格是否变过 |
| `ClearDirty()` | View 层同步完成后清空 |
| `Version` **重建时归零** | "版本号变小"是"整张图被换过"的可靠信号，触发全量刷新 |

效果：无变化时每帧开销约等于零；Phase 2 草蔓延/生物移动时，只刷新真正动过的格子。

### 12.9 Phase 1 的文件清单

| 文件 | 层级 | 职责 |
|---|---|---|
| `Core/GridPos.cs` | Core | 坐标 struct，无 GC 哈希 |
| `Core/TileType.cs` | Core | 地形枚举 |
| `Core/CellState.cs` | Core | 格子状态（地形 + 草 + 占据者） |
| `Core/GridUtility.cs` | Core | 索引 / 四邻域 / 洪水填充 / 最大连通块，零 GC |
| `Core/ValueNoise.cs` | Core | 整数哈希值噪声，跨平台确定性 |
| `Core/IslandGenerator.cs` | Core | 岛屿生成 5 步流水线 |
| `Core/IslandGrid.cs` | Core | **唯一真相来源** + 统计 + 脏格子追踪 |
| `Core/IslandConfigData.cs` | Core | 生成参数与配色的纯数据载体 |
| `Config/IslandConfig.cs` | Config | ScriptableObject，Inspector 调参 |
| `View/WorldGrid.cs` | View | **坐标转换唯一入口**（架构铁律 4） |
| `View/RuntimeSprite.cs` | View | 1x1 白色 Sprite 工厂（Phase 1 占位美术） |
| `View/CellPalette.cs` | View | 把 Core 的 0xRRGGBB 翻译成 Color |
| `View/TileView.cs` | View | 单格视觉，只读不改数据 |
| `View/GridView.cs` | View | 256 个 Tile 的创建与脏同步 |
| `View/CameraRig.cs` | View | 构图 + 像素完美对齐 |
| `Bootstrap/GameBootstrap.cs` | Bootstrap | 场景唯一入口，只做装配 |

### 12.5 参数默认值（Inspector 可调，无需改代码）

| 参数 | 默认值 | 说明 |
|---|---|---|
| `width` / `height` | 16 / 16 | 网格尺寸 |
| `seed` | 0（0 = 随机） | 0 时启动随机取种子，并在 HUD 上显示实际种子 |
| `landRatio` | 0.45 | 目标陆地占比，算法自动逼近 |
| `coastNoiseStrength` | 0.35 | 海岸线扰动强度（越大越破碎） |
| `coastNoiseScale` | 0.18 | 噪声频率（越大越细碎） |
| `octaves` | 3 | 噪声倍频程数 |
| `borderWater` | true | 强制最外圈为海水 |
| `ppu` | 16 | 每单位像素数（像素风基准） |
| `secondsPerDay` | 12 | 一天的真实秒数 |
| `secondsPerTick` | 1 | 一次生物行动的真实秒数 |
| `totalDays` | 15 | 一局总天数 |

---

## 13. 待办与风险

| # | 事项 | 影响 | 计划 |
|---|---|---|---|
| R1 | **物种存活稳定性存疑**：草扩散占满陆地会挤死兔，狼大概率在 15 天内饿死或把兔吃绝 | 高：直接决定玩法是否成立 | Phase 8 处理。预备方案：饥荒缓冲、迁徙、最低种群阈值、狼的替代食物 |
| R2 | 像素完美对齐与屏幕宽高比（16:9 / 16:10 / 4:3）的适配 | 中：不同显示器上构图不同 | Phase 1 的 `CameraRig` 已按"按高对齐 + 整数像素"处理 |
| R3 | 无 Pixel Perfect 包，斜向移动可能出现非整数像素抖动 | 低：像素风观感 | Phase 7 若观感不佳再装包 |
| R4 | ASCII 调试地图在 Phase 2 之后会不够用 | 低：调试效率 | Phase 2 视情况加 grid 快照导出为 PNG |
