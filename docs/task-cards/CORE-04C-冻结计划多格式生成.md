# CORE-04C：冻结计划多格式生成

## 状态

Planned。依赖 CORE-04B 已交付的 Build-Ready Frozen Plan v2；本卡尚未实现。

## 输入与目标

仅消费冻结且可构建的计划，按明确输出选择生成并验证 SceneDraft、单体 GLB、Scene Package 和本地 Cartesian 3D Tiles。每个作业使用独立输出根、staging 和原子发布，支持取消、失败和部分成功状态。

## 非目标

不重新解析 CAD、不改变冻结语义、不伪造 DWG 支持，不在本卡实现 Avalonia、Viewer、HLOD、增量构建、IDTS 集成或未验证的多格式能力。

## 退出条件

生成器调用次数、产物验证、路径安全、失败清理和取消收敛均有自动化证据；Build 能力只有在这些证据具备后才可从 Planned 升级。
