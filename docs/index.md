# inventory-organizer 文档目录

SPT 一键整理仓库 mod 的项目文档。

- [requirements.md](requirements.md)：用户原始需求与待澄清问题
- [arch.md](arch.md)：总体设计，模块边界、依赖方向与核心数据流
- [feats/tag-grammar.md](feats/tag-grammar.md)：tag 规则语法契约
- [feats/packing.md](feats/packing.md)：排布目标、收纳空间选择、锁与求解预算
- [plans/mvp.md](plans/mvp.md)：MVP 实施计划
- [plans/collection-optimization.md](plans/collection-optimization.md)：
  多网格收纳、阶段预算与堆叠规划优化草稿

## 调研报告

- [reports/fold-and-lock-detection.md](reports/fold-and-lock-detection.md)：
  SPT 4.1 客户端里可折叠、Pin/Lock 与原生排序的判定来源
- [reports/cpsat-runtime-feasibility.md](reports/cpsat-runtime-feasibility.md)：
  CP-SAT / OR-Tools 能否在客户端或服务端运行，规模实验与替代方案
