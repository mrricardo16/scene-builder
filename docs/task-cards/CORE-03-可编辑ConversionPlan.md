# CORE-03：可编辑 Conversion Plan

## 目标

建立 Analysis → `ConversionPlanDraft` → Validation → `FrozenConversionPlan` 的可版本化计划流程。

## 前置、输入与契约

依赖 CORE-02。Draft 至少包含 PlanId、Revision、Dirty、ValidationStatus、SourceAnalysisId、CreatedAt、UpdatedAt。Frozen Plan 是 Build 的唯一输入并且不可变。

## 范围与非目标

支持受控单位确认、局部原点、Z Offset、平面旋转、修复开关/容差、现有规则契约、已支持高度、显式资产绑定和既有输出参数。不得修改原始 CAD、重写 `CadRuleEngine`、猜测资产文件、伪造未支持几何字段或让 Blender 读取可变 UI 状态。

## 验证与退出

验证修订不覆盖旧版本、无效计划不可冻结、冻结后输入确定、规则和资产映射复用现有安全边界。退出时 CORE-04 可只消费冻结快照。
# 实施记录（2026-07-30）

已从验证后的 Analysis Artifact 创建确定性 Plan Draft、独立 revision、validation Artifact 和不可变 Frozen Plan，并接入共享 Host 与 CLI。没有启动 Blender、读取 DXF 或生成三维产物；Build 继续属于 CORE-04。
