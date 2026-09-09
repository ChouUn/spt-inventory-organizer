# MVP：一键整理仓库

> Parent: [../index.md](../index.md)
> Status: 进行中
> 依据：[requirements.md](../requirements.md)、[arch.md](../arch.md)、
> [feats/tag-grammar.md](../feats/tag-grammar.md)、`reports/`

## 目标

点击容器面板的排序按钮，对该容器完成折叠、按 tag 收纳、排布。
完成后玩家能观察到：可折叠武器已折叠，物品按规则进了容器，容器内布局紧凑，
Pinned 与 Locked 物品原地不动。

## 范围

### 包含

工程骨架、tag 解析与测试、tag 编辑提示与长度放宽、替换排序按钮、
快照与游戏适配、折叠、收纳、启发式排布、CP-SAT 排布。

### 不包含

服务端组件、FiR 条件、预览与撤销、战局内可用、角色装备整理、发布打包与版本检查。

## 实现方案

- 三个工程都面向 net481：OR-Tools 没有 netstandard2.0 目标，Core 只能选 net462 以上。
  测试工程用 xunit，在 Windows 的 dotnet 上跑。
- Plugin 用 RID win-x64，OR-Tools 原生库随构建进入输出目录，整目录部署到
  `BepInEx/plugins/ChouUn.InventoryOrganizer/`。游戏程序集经 `SPT_DIR` 引用，不入库。
- Bootstrap 在 Awake 最早处 `SetDllDirectory(插件目录)`，依据是 spike 的结论。
- 排序按钮的替换点是 `GridSortPanel` 的点击处理，依据是折叠与锁的调研报告。
- 收纳阶段先用面积可行性判断，排布阶段先上启发式；CP-SAT 作为最后一步接入大件。

## 实施步骤

- [x] 1. 工程骨架
  - 结果：`src/Core`、`src/Plugin`、`tests/Core.Tests`、解决方案、部署脚本；
    Plugin 的 Awake 只做原生路径准备与日志。
  - 验证：构建与测试通过；游戏日志出现插件加载行（用户验收）。
- [ ] 2. TagGrammar
  - 结果：按 feat 实现解析器，测试覆盖 feat 中每条规则、每种错误和全部示例。
  - 验证：测试通过。
- [ ] 3. tag 编辑补丁
  - 结果：输入框上限放宽；保存时解析，成功复述规则，失败提示错误。
  - 验证：游戏内编辑 tag，看到提示（用户验收）。
- [ ] 4. 排序按钮入口与快照
  - 结果：排序按钮改为调用 Orchestrator；GameAdapter 读出快照；
    本步 Orchestrator 只把快照写进日志。
  - 验证：点击按钮，日志中的快照与仓库实际内容一致（用户验收）。
- [ ] 5. 折叠阶段
  - 结果：折叠计划、simulate、提交；Locked 物品不动。
  - 验证：点击后可折叠武器折叠，被锁武器不变（用户验收）。
- [ ] 6. 收纳阶段
  - 结果：Collector 按优先级调度，尊重容器接受性与面积可行性；
    Pinned 与 Locked 不动，无匹配留原地。
  - 验证：Core 测试覆盖调度顺序与跳过规则；游戏内物品按规则进容器（用户验收）。
- [ ] 7. 启发式排布
  - 结果：HeuristicPacker 与装箱接口；固定占位不动。
  - 验证：测试覆盖不重叠、不越界、固定占位；游戏内容器布局紧凑（用户验收）。
- [ ] 8. CP-SAT 排布
  - 结果：CpSatPacker 处理大件，1×1 交给启发式，带时间上限。
  - 验证：测试与启发式对比利用率不降；游戏内耗时在上限内（用户验收）。

## 验收

- Core 测试全部通过。
- 用户在游戏内完成一次编辑 tag 与一次整理仓库的验收。

## 风险与阻塞

- 多步变更能否合成一个网络事务未查，报告只看了客户端。
  影响提交粒度与失败时的回滚方式，在步骤 5 首次遇到时决定并回写 arch 的约束。
- 原生排序按钮带确认框，替换后是否保留在步骤 4 决定。

## 长期文档

- `feats/tag-grammar.md` 随实现同步；实现偏离时先确认再改契约。
- 提交粒度定下后回写 `arch.md` 关键约束。
- 需求 F 项如有偏离，先向用户确认。
