# SB-11B：空间分区与多 GLB 场景包

## 输入与产物

输入为已验证 `SceneDraft`、Blender 资产上下文和固定分区/发布策略；产物为多个已验证 GLB、严格 `scene-package.json` 与动态节点索引。

## 门禁

- 固定米制 XY 网格、稳定 ID、每对象单一归属；跨区不切割不复制，超大对象进入 Global 或按策略失败。
- 每个 GLB 独立调用既有 Blender 流水线并通过 `BinaryGlbValidator`。
- Index 仅引用已验证相对产物；staging 通过验证后原子发布。
- 分区失败隔离；仅显式允许时发布部分包；取消不发布。
- 不修改 SceneDraft、源资产、JobReport v0；不实现缓存、并行、LOD 或 3D Tiles。
