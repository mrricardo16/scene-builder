# SB-12A：3D Tiles 1.1 场景索引生成

## 输入、产物和门禁

输入为经 `ScenePackageValidator` 验证的场景包目录；输出仅为同目录原子发布的 `tileset.json`。每个成功且带 GLB 的分区映射为一个直接引用 `partitions/*.glb` 的 leaf，Root 无 content、`refine="ADD"`，并使用全部 leaf 的 ContentBounds 并集。

- `asset.version` 固定 `1.1`；坐标为 Local Cartesian meters、Z-up。
- Box 固定为轴对齐 12 数形式，退化轴仅扩大 culling envelope。
- URI 必须复用场景包的安全相对 artifactPath；所有 GLB 再次经现有 Validator 验证。
- 只在临时 JSON 自校验通过后移动为 `tileset.json`；默认拒绝已有输出与 overwrite。
- 部分场景包可由策略发布成功子集；失败分区不进入 leaf。

## 非目标

不实现地理配准、Root Transform、b3dm/i3dm、HLOD、LOD、隐式分块、Metadata、Cesium/IDTS 前端、HTTP 服务或重新调用 Blender。

SB-12A 是 [大厂区 3D Tiles 产品规格与验收基线](../大厂区3DTiles产品规格与验收基线.md) 的已验证两层基础，不是大型厂区 HLOD 或 Viewer 产品实现；后续能力由 LARGE-00 至 LARGE-04 定义。

## 证据

`SceneBuilder.Tiles.Tests` 覆盖 Box、完整/部分包、URI 越界与覆盖保护。`SceneBuilder.Blender.SmokeTest --mode tileset` 使用真实 Blender 创建 2 个常规和 1 个 Global 分区，再验证 3 个 leaf、Tileset 和 GLB。
