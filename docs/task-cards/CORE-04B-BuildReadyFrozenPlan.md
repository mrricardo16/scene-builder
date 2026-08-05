# CORE-04B：Build-Ready Frozen Plan

## 状态

Completed。CORE-04B 只交付 Build-Ready Frozen Plan v2 数据链路；不交付 CORE-04C 生成器。

## 输入与目标

消费已验证的 Analysis v2 与 Build Input Snapshot，冻结 SnapshotId、ContentHash、规则/分类快照、修复选择、单位、坐标变换、高度、资产绑定、输出和分区等完整语义配置，形成明确的 Frozen Plan v2。

## 非目标

不重新读取 DXF、不猜测资产或 UI 参数、不静默升级 Frozen Plan v1，不启动 Blender 或发布三维产物。

## 退出条件

旧 Frozen Plan v1 继续被 Gate 拒绝；只有 Snapshot 引用、内容哈希和完整 Build Configuration 均通过校验时，Frozen Plan v2 才可进入 CORE-04C。当前 `BUILD_READY_FROZEN_PLAN` 为 Available，但 `BUILD`、`BUILD_GLB`、`BUILD_SCENE_PACKAGE`、`BUILD_3D_TILES` 仍为 Planned。

实现包含严格 UTF-8/JSON 回读、内容寻址的 Plan Asset Resource Import、staging/原子发布、确定性 FrozenPlanId、v1 兼容读取、v2 Readiness 诊断和零三维生成器调用验证。CLI 只扩展 plan 摘要，不新增 `build` 命令。
