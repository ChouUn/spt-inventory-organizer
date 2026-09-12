# Stash Master —— 一个固执己见的仓库管理器

[English](README.md) | **简体中文**

- **一键整理**：折叠、堆叠、收纳到容器、排序。
- **塞满容器**：惊人的空间利用率和自适应收纳，让零碎空位也派上用场。
- **紧凑布局**：同类物品尽可能聚在一起，不再一半在仓库顶、一半在仓库底。

## 痛点

是否受够了莫名其妙的排序失败？

五把枪明明已经放进武器箱，底部还空着两行，点击排序却报错：

![武器箱底部还有两行空位，排序却提示失败][sort-failure]

放得下，却排不好：按顺序摆进去的枪，可能堵住后面长枪需要的空间。
Stash Master 会一起考虑物品的位置和朝向，必要时挪开前面的物品，重新腾出空间。

另用三组武器箱组合做了对照，原生与 Stash Management Helper 各有无法完成排序的情况，
Stash Master 在三组中都找到了完整布局。每组的尺寸、图例和复现结果见
[武器箱排序案例][comparison]。

![三组武器箱输入及原生、Stash Management Helper、Stash Master 的排序结果][case-inputs]

✓ 完成排序；× 排序失败，保留原布局。Stash Management Helper 使用下文所列配置。

## 与原生、Stash Management Helper 的对照

### 护甲放在末尾，也能排紧

原生排序把护甲放在最末尾，大量空洞散落在装备之间，空间利用率很糟糕。
换成 Stash Master，护甲仍在末尾，排列却紧致得多：

| 原生 | Stash Master |
| --- | --- |
| ![原生：装备之间散布空洞][armor-native] | ![Stash Master：紧凑排列][armor-sm] |

### Stash Management Helper 的默认排序

Stash Management Helper 默认把护甲放在最前面，已经排得不错了。
不过，钥匙扣、弹匣、子弹这些“小可爱”还是插进了装备之间的空洞：

Stash Master 使用 `Backpacks > Armor > Rigs`，得到相近的装备布局，
装备之间不再夹杂钥匙、弹匣和子弹：

| Stash Management Helper | Stash Master |
| --- | --- |
| ![SMH：红框中的零碎物品][armor-smh] | ![SM：背包、护甲、胸挂靠前][armor-sm-custom] |

### 同一仓库的测量

同一份散乱仓库，先完成折叠、堆叠和收纳，再让三种算法整理相同的物品：

| 排布结果 | 原生 | Stash Management Helper | Stash Master（我们） |
| --- | ---: | ---: | ---: |
| 仓库占用行数 | 62 | **61** | **61** |
| 同类最大行间距之和 | 106 | 212 | **59** |

两项都是越小越好。行间距按父类别统计：同类物品最下方与最上方占用行之差，再逐类相加。
固定物品保留原位，不计入这两项指标，因此占用行数不代表底部一定完全空出。

Stash Management Helper 使用容量、面积优先配置，未开启类型排序。
这组数据中，原生和它的计算更快；我们用更多计算时间换取紧凑的布局。
[完整测量、耗时与配置][timings]。

## 开始整理

给**钱箱**打上 `@o` 标签，再点击**仓库的排序按钮**，
按提示确认，仓库里散放的钱就会自动收进钱箱。
点击某个容器的排序按钮，则整理那个容器里的物品。
折叠、合并堆叠、收纳和排序会依次完成，结束时显示整理结果。

### 标签规则

`@o` 让容器接收它能装的所有物品；加上条件，就只收符合条件的物品。

| 容器 | 标签 | 收纳效果 |
| --- | --- | --- |
| 钱箱 | `@o` | 收纳散放的钱 |
| 狗牌包 | `@o#1` | 第 **1** 个收纳狗牌，防止被垃圾箱抢去 |
| 弹药箱 | `@o n:5.45;` | 只收 5.45 口径弹药 |
| 弹药箱 | `@o n:5.45 \|\| n:5.56;` | 收纳 5.45 或 5.56 口径弹药 |
| 物品箱 | `@o 弹匣 && n:5.45;` | 只收 5.45 口径的弹匣 |
| SCAV 垃圾箱 | `@o !n:狗牌;` | 收纳杂物，排除狗牌 |

```text
@o[#优先级] [条件;]
```

方括号内为可选部分，实际标签不写方括号。

- **名称和类别**：`n:5.45` 收名称或简称里含 `5.45` 的物品；
  不加 `n:` 时，写游戏手册里的完整类别名，也会收这个类别下的所有子类。
  名称和类别用当前游戏语言，大小写都可以。
- **优先级**：在 `@o#` 后紧接自然数。所有容器按这个数字决定收纳先后，
  `#1` 比 `#2` 先执行，没写数字的最后执行。
  数字相同的先后不固定；装不下的物品会继续尝试后面的规则。
- **组合条件**：`&&` 表示“并且”，`||` 表示“或者”，`!` 排除后面指定的物品。
  同时写 `&&` 和 `||` 时，先判断 `&&`；不能用括号改变判断顺序。
- **空格和分号**：`@o` 和 `n:` 用小写。条件与前面的 `@o` 或 `@o#1` 之间留空格，
  末尾加英文分号 `;`；只写 `@o` 或 `@o#1` 时，分号可省略。

一个标签也能写多条规则：给弹药箱写 `@o#1 n:5.45; @o#5`，
先按 `#1` 收 5.45 口径弹药，再按 `#5` 收其他弹药。
容器名称可以写在 `@o` 前，不影响规则。
保存标签时会显示每条规则的含义；如果写错，会指出错误，修正前整个标签都不生效。

### 类型顺序

默认启用，沿用 SMH 的物品类型。首次整理后，打开
`BepInEx/config/com.chouun.stashmaster.category-order.json`，
按想要的先后调整 `itemTypeOrder`。
首次生成配置时，中文游戏使用中文类型名，其他语言使用英文名。
例如让护甲、胸挂、武器依次靠前：

```json
{
  "enabled": true,
  "itemTypeOrder": ["护甲", "胸挂", "枪械"]
}
```

没写的类型排在后面。保存后，下次整理生效，**无需重启游戏**。
中英文可混写，已有配置无需改动，切换游戏语言后也能继续使用。

<details>
<summary>推荐顺序与物品类型（默认配置）</summary>

| 英文类型名 | 中文类型名 |
| --- | --- |
| `Containers` | 容器 |
| `Headsets` | 耳机 |
| `Headgear` | 头部装备 |
| `NightAndThermalVision` | 夜视与热成像 |
| `HeadgearArmor` | 头盔装甲 |
| `Eyewear` | 眼镜 |
| `Armor` | 护甲 |
| `Rigs` | 胸挂 |
| `BallisticPlates` | 插板 |
| `Backpacks` | 背包 |
| `Weapons` | 枪械 |
| `Magazines` | 弹匣 |
| `Ammo` | 弹药 |
| `Grenades` | 投掷物 |
| `Meds` | 医疗 |
| `Food` | 食物 |
| `Drink` | 饮料 |
| `Facecovers` | 面罩 |
| `Armband` | 臂章 |
| `Melee` | 近战 |
| `Mods` | 配件 |
| `RepairKits` | 维修包 |
| `SpecialEquipment` | 特殊装备 |
| `Barter` | 杂物 |
| `Keys` | 钥匙 |
| `Money` | 货币 |
| `Info` | 情报 |

</details>

### 整理规则与限制

- **容器可以嵌套**：箱子放在另一个箱子里，也能按标签收纳。
  当前容器和写了规则的子容器会合并堆叠、重新排列物品；
  没写规则的容器里只会折叠物品，不会合并或换位置。
- **沿用游戏内置的 Pin／Lock**：固定或锁定的物品不会被挪走。
  Pin 固定的堆叠仍能补充物品；Lock 锁定的物品不会合并，锁定容器内部也不整理。
- **先补满，再找空位**：两个箱子里的半堆可以合到一处；箱子没有空格时，
  只要已有堆叠没满，仍能继续补充。
- **箱子原有的物品保留**：需要时会重新摆放，为新物品腾位置；仍装不下的继续找其他容器。
  标签不会突破容器自身的限制，例如弹药箱依然不能装枪。

## 兼容性

- **[UI Fixes][ui-fixes]**：可以搭配使用。排序与堆叠由 Stash Master 统一处理，
  不用手动关闭它的“排序前合并堆叠”；其他功能、配置与 FiR 混堆设置保留。
- **[Stash Management Helper][smh]**：目前尚不完全兼容。它的右键价格／重量排序
  也会变成我们的一键整理，并可能留下未恢复的临时排序设置。

Stash Master 自带折叠和堆叠功能，不依赖以上 mod。

## 灵感与感谢

我曾长期使用并受益于这些 mod，感谢它们的作者和维护者：

- [IOF: Inventory Organizing Feature][iof]：标签规则与递归收纳。
- [Stash Management Helper][smh]：自定义排序、自动折叠与堆叠。
- [UI Fixes][ui-fixes]：库存操作体验与排序前自动堆叠。

这个项目最初只是想重构 IOF，后来不知不觉把整个整理流程都打通了。
最终的策略带着我自己的审美和品味，也离不开这些前作的启发。

[开发说明](docs/development.md) · [文档目录](docs/index.md)

[sort-failure]: assets/images/failed-sort-example.png
[case-inputs]: assets/images/weapons-sort-comparison.svg
[armor-native]: assets/images/armor-sort-native.png
[armor-sm]: assets/images/armor-sort-stash-master.png
[armor-smh]: assets/images/armor-sort-smh.png
[armor-sm-custom]: assets/images/armor-sort-stash-master-custom.png
[comparison]: docs/reports/weapon-sort-boundaries.md
[timings]: docs/reports/sort-comparison.md
[iof]: https://sp-mod.com/mod/2960/iof-inventory-organizing-feature
[smh]: https://sp-mod.com/mod/1861/stash-management-helper
[ui-fixes]: https://sp-mod.com/mod/1342/ui-fixes
