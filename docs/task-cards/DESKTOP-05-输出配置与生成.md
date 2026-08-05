# DESKTOP-05：输出配置与生成

## 目标

从已冻结计划选择单体 GLB、Scene Package、3D Tiles，并启动统一 Build。

## 范围与退出

显示输出目录、单体 GLB/Scene Package/3D Tiles 输出选择、CellSizeMeters、Grid Origin、MaximumIntersectedCellsPerObject、LargeObjectBehavior、PublishPartialPackage 与 RootGeometricErrorMeters 的范围和错误。为大厂区路线预留明确的输出 Profile 与验收预算入口，但 HLOD、Tile 预算、资产优化、缓存和 Viewer 验收在 LARGE-00 至 LARGE-04 完成前均为 Planned，不能显示为可用选项。不得重新猜测 UI 参数、覆盖旧作业、直连 Blender/Tiles 或在未验证产物前显示可预览。多输出选择、冻结输入和失败状态可验证即退出。
# CORE-04C 复用边界

Desktop 输出页只编辑并冻结计划，不在 UI 重建规则、Repair、资产或坐标语义；执行时复用统一 Build Handler。当前 Build CLI 已接入，Avalonia 页面仍为后续工作，且在真实 SmokeTest 前不得把生成选项显示为已验证生产能力。
