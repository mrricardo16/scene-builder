# CORE-04C：冻结计划多格式生成

## 状态

Planned。依赖 CORE-04B 已交付的 Build-Ready Frozen Plan v2；本卡尚未实现。

## 输入与目标

仅消费冻结且可构建的计划，按明确输出选择生成并验证 SceneDraft、单体 GLB、Scene Package 和本地 Cartesian 3D Tiles。每个作业使用独立输出根、staging 和原子发布，支持取消、失败和部分成功状态。

## 非目标

不重新解析 CAD、不改变冻结语义、不伪造 DWG 支持，不在本卡实现 Avalonia、Viewer、HLOD、增量构建、IDTS 集成或未验证的多格式能力。

## 退出条件

生成器调用次数、产物验证、路径安全、失败清理和取消收敛均有自动化证据；Build 能力只有在这些证据具备后才可从 Planned 升级。
# CORE-04C 本地实现记录（2026-08-05）

已实现本地 `BuildFrozenPlanHandler`、Snapshot 到 SceneDraft 映射、冻结规则重分类、选择性 Repair、显式资产传递、单体 GLB/Scene Package/3D Tiles 依赖编排、`build-000N` 隔离、staging 原子发布、CLI `build`、取消/超时/未配置状态和 `build-result.json`。Build 不重新解析 CAD，也不调用 Analyze。

变换顺序固定为：分析局部米制坐标 → ExplicitOffset 减 LocalOrigin → Z 轴 Yaw → ZOffset；此顺序已写入设计文档并由映射测试覆盖。由于当前机器 `scene-builder doctor` 报告 Blender 未配置，`BUILD*` 能力保持 Planned，不能宣称真实 Blender SmokeTest 已通过。
