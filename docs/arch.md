# inventory-organizer 总体设计

> Parent: [index.md](index.md)
> Related: [requirements.md](requirements.md)

一键整理仓库的 SPT 客户端 mod。本文只定模块边界、依赖方向和核心数据流，
tag 语法与各阶段算法的精确契约放 `feats/`，实施顺序放 `plans/`。

## 术语

| 术语 | 含义 |
| --- | --- |
| 收纳 | 按 tag 规则决定物品进哪个容器，并移动过去 |
| 排布 | 在一个网格内决定每个物品的位置与转置，减少碎片空位 |
| 整理 | 折叠、收纳、排布三个阶段的一次完整执行 |
| 规则 | 一条 `@o[#<order>] <expr>;`，含优先级与匹配表达式 |
| 快照 | 从游戏读出的容器树、物品描述与锁状态的纯数据副本 |
| 计划 | 纯算法算出的一组待执行变更：折叠哪些、移到哪、放在哪 |

## 设计目标

- 纯算法与游戏隔离：解析、匹配、调度、排布不依赖 Unity 与游戏程序集，
  能在普通 .NET 测试里跑。
- 先模拟再落地：任何变更先经游戏的 simulate 校验，通过后才提交。
- 阶段顺序固定：折叠、收纳、排布的先后由编排层固定，阶段之间不互相调用。
- 求解器可替换：CP-SAT 与启发式实现同一装箱接口，启发式同时是退路。
- 游戏规则只问游戏：可折叠、锁、容器过滤的判定来自游戏，本 mod 不维护清单。

## 模块划分

两个程序集加一个测试工程。Core 不引用任何游戏或 Unity 程序集。
目录用短名，程序集名在 csproj 里指定。

| 目录 | 程序集 | 内容 |
| --- | --- | --- |
| `src/Core/` | `ChouUn.InventoryOrganizer.Core.dll` | 纯算法与编排 |
| `src/Plugin/` | `ChouUn.InventoryOrganizer.dll` | BepInEx 插件：适配、界面、启动 |
| `tests/Core.Tests/` | 测试 | Core 的单元测试 |

| 模块 | 程序集 | 职责 |
| --- | --- | --- |
| TagGrammar | Core | tag 文本解析为规则列表；错误带位置，供编辑提示使用 |
| Matching | Core | 规则表达式对物品描述求值 |
| Collector | Core | 按规则优先级全局调度，决定每个物品进哪个容器 |
| Packing | Core | 装箱接口；CpSat 实现处理大件，Heuristic 实现处理 1×1 与退路 |
| Orchestrator | Core | 固定顺序驱动三阶段：取快照、算计划、模拟、提交 |
| Ports | Core | 编排层对外的接口：读快照、模拟与执行变更、提交事务、通知 |
| GameAdapter | 插件 | 实现 Ports：把游戏对象转成快照，把计划翻译成游戏操作 |
| Patches | 插件 | Harmony 补丁：替换原生排序按钮，放宽 tag 长度，保存时解析并提示 |
| Bootstrap | 插件 | 启动顺序：先准备原生库搜索路径，再注册补丁与配置 |

## 依赖关系

```mermaid
flowchart TB
    subgraph Plugin[ChouUn.InventoryOrganizer 插件]
        Bootstrap --> Patches
        Bootstrap --> GameAdapter
        Patches --> Orchestrator
        GameAdapter -. 实现 .-> Ports
    end
    subgraph Core[ChouUn.InventoryOrganizer.Core]
        Orchestrator --> Ports
        Orchestrator --> Collector
        Collector --> Matching
        Collector --> Packing
        Patches --> TagGrammar
        Collector --> TagGrammar
    end
    Packing --> OrTools[Google.OrTools]
    Plugin --> Game[BepInEx / Unity / Assembly-CSharp]
```

- 插件依赖 Core，Core 不依赖插件。
- Core 内只有 Packing 的 CpSat 实现依赖 Google.OrTools。
- 游戏程序集只被插件引用。

## 数据流：一次整理

用户点击容器面板上的排序按钮，触发对该容器的整理。

```mermaid
flowchart TB
    Click[Patches 截获排序按钮] --> Snap[GameAdapter 读快照]
    Snap --> Fold[Orchestrator 阶段一：折叠计划]
    Fold --> SimF{模拟通过?}
    SimF -- 否 --> Abort[中止并通知]
    SimF -- 是 --> Collect[阶段二：Collector 收纳计划]
    Collect --> SimC{模拟通过?}
    SimC -- 否 --> Abort
    SimC -- 是 --> Pack[阶段三：Packing 逐容器排布]
    Pack --> SimP{模拟通过?}
    SimP -- 否 --> Abort
    SimP -- 是 --> Commit[GameAdapter 提交事务]
    Commit --> Notify[通知结果]
```

- 快照是纯数据：容器树、每个物品的类别链、名称、FiR、尺寸、可折叠、锁状态、tag 文本。
- Collector 决定「进哪个容器」时可向 Packing 询问可行性，
  排布阶段再决定「放在哪」。两阶段的具体配合方式属于算法设计，不在本文约束。
- Pinned 与 Locked 物品在快照中标出，Collector 与 Packing 都把它们当作固定占位。
- 每个阶段的计划先由 GameAdapter 以 simulate 模式执行，任一步失败即中止。

## 数据流：编辑 tag

保存 tag 时 Patches 调 TagGrammar 解析，解析结果或错误位置直接呈现给用户。
这条路径不经过 Orchestrator，也不改动任何物品。

## 关键约束

- Core 不得引用 BepInEx、UnityEngine、Assembly-CSharp。游戏事实只能以快照数据进入。
- 插件不得包含匹配、调度、排布逻辑；发现需要这类判断时，把数据加进快照交给 Core。
- 可折叠、锁状态、容器是否接受某物品，由 GameAdapter 问游戏后写进快照，
  Core 不复制游戏规则。
- 未通过 simulate 的变更不得提交。
- 触碰任何 Packing 类型之前，Bootstrap 必须已把原生库目录加入搜索路径；
  失败的 P/Invoke 初始化在进程内不可恢复。
- 阶段顺序与阶段内算法的选择封装在 Core，插件不感知。
