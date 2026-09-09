# refs-local

参考资料目录：SPT 安装、相关 mod 项目及其源码副本。

- 本目录除本文件外不纳入版本控制，见仓库根目录 `.gitignore`。
- 本机路径（SPT 安装目录、本地项目目录）记在本目录的 `local.md`，该文件被忽略。
- 各源码副本是独立的 git clone，更新直接在子目录内 `git pull`。

## SPT 安装

- SPT 4.1：当前版本。
- SPT 4.0：旧版本。

两套安装的 `BepInEx\plugins\` 下都装有大量编译好的 mod。
这些 mod 一般都能找到 GitHub 源码，需要时按插件名查找。

## 相关项目

### IOF 4.0 port（用户自己的）

- 位置：见 `local.md`
- 说明：Inventory Organizing Features（IOF）由用户 port 到 SPT 4.0 的版本。
  是本项目的灵感来源，但使用体验不佳，有优化空间。

### inventoryorganizingfeatures/

- 上游：<https://gitlab.com/flir063-spt/inventoryorganizingfeatures>
- mod 页：<https://sp-mod.com/mod/2960/iof-inventory-organizing-feature>
- 说明：IOF 的 SPT 4.1 版本，由 flir port。
  用 `@` 开头的物品 tag 表达整理规则。
- 快照：`ea30bf1`（2026-08-27，prep v1.9.0）

### AutoDeposit/

- 上游：<https://github.com/tyfon7/AutoDeposit.git>
- mod 页：<https://sp-mod.com/mod/1469/autodeposit>
- 说明：另一个收纳相关项目。点按钮把身上的物品转移到仓库中已含同类物品的容器，
  不负责自动分类。
- 快照：`e56c8f3`（2026-08-12，fix guid format）

## 候选（未放入）

SPT 4.1 安装的 `BepInEx\plugins\` 中已装、与库存整理相关的 mod。
需要参考时找到源码 clone 到本目录，并补到「相关项目」：

- `StashManagementHelper`：仓库排序类。
- `DrakiaXYZ-QuickMoveToContainer`：物品快速移入容器。
- `Tyfon.UIFixes`：大量库存界面修补，含物品操作相关补丁。
- `MergeConsumables`：合并消耗品堆叠。
- `acidphantasm-moretagcolours`：扩展 tag 颜色，涉及 tag 读写。

## 添加新参考

1. 在本目录 `git clone` 上游仓库。
2. 在「相关项目」补一节：上游、mod 页、说明、快照。
3. 涉及本机路径的，只写进 `local.md`。
