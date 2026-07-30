# DESKTOP-09：3D Tiles 与 Cesium 边界

## 目标

在 SB-11B 与 SB-12 均已验证后，独立评估桌面端对 3D Tiles/Cesium 的查看或适配边界。

## 验收

- 先完成独立 POC、坐标/分区/资源释放/发布验证，再决定是否进入产品任务。
- 未验证或工具未配置时，仅显示 NotConfigured/未支持证据，不生成伪造 tileset 或 UI 成功状态。
- 与现有 `SceneDraft`、资产映射、`JobReport v0` 保持单向适配，不将 Cesium 类型引入 Domain。

## 非目标

本卡不交付当前 MVP 功能；DESKTOP-09 不能被用作现有 3D Tiles/Cesium 支持声明。
