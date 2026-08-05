# CORE-04A：版本化 Analysis Build Snapshot

## 状态

Completed。Analysis v2 和 Build Input Snapshot v1 已实现；Frozen Plan v1 仍由 `FROZEN_PLAN_NOT_BUILD_READY` Gate 拒绝。

## 范围

- Analyze 默认发布 `analysis/cad-analysis.json` 与 `analysis/build-input-snapshot.json`。
- Snapshot 保存标准化几何、轮廓、Repair 候选、Classification Subject、分析时分类和可确认资产候选。
- 使用稳定 ID、canonical UTF-8 SHA-256 ContentHash、staging、严格回读和原子发布。
- 保持 Analysis v1 读取、CORE-03 `plan create` 和三维流水线隔离。

## 非目标

不实现 Frozen Plan v2、SceneDraft Mapper、Build CLI、Blender、GLB、Scene Package、3D Tiles、Avalonia、DWG、HLOD 或资产猜测。

## 验证

目标测试、全量 build/test、CLI 两次 Analyze 确定性、严格 UTF-8/JSON、路径和引用校验、`git diff --check` 均须通过。
