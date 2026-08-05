# CORE-04：冻结计划与多格式生成

## 当前拆分状态

- CORE-04 Gate：Completed。Frozen Plan v1 仍会被 `FROZEN_PLAN_NOT_BUILD_READY` 拒绝。
- CORE-04A：Implemented。Analyze v2 发布经校验的 Build Input Snapshot v1；它不使 Frozen Plan v1 Build Ready。
- CORE-04B、CORE-04C：Planned。不得在 04A 中创建 SceneDraft、启动 Blender 或生成任何三维产物。

## 目标

从 `FrozenConversionPlan` 统一协调单体 GLB、Scene Package 和 3D Tiles 1.1 的可选输出。

## 前置、输入与契约

依赖 CORE-01 与 CORE-03，消费冻结计划、受控输出目录、工具配置和取消令牌。复用现有 Blender、Package 和 Tiles 组件及其验证器。

## 范围与非目标

Build 不重新解析 CAD、不猜测 UI 参数、不覆写旧作业产物。它只发布通过验证的产物，并以明确状态报告失败、部分成功、取消和未配置。

CORE-04A 仅保存标准化 CAD 几何、轮廓、修复候选、重分类事实和分析时分类结果。快照采用稳定 ID 与 SHA-256 canonical content hash，发布为 `analysis/build-input-snapshot.json`，同时 Analysis v2 摘要只携带其轻量描述符。

## 验证与退出

验证每种输出均可选择、产物隔离、取消可收敛、失败不伪造成功。退出时 Desktop 可安全接入生成，但预览仍须分别完成 DESKTOP-00、07、08。
