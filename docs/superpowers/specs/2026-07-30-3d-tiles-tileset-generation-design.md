# SB-12A：3D Tiles 1.1 场景索引生成设计

## 目标与边界

SB-12A 将已验证的 `scene-package.json` 封装为本地米制坐标的 3D Tiles 1.1 `tileset.json`。它只读取现有场景包和 `partitions/*.glb`，不重新运行 Blender、解析 DXF、计算分区或修改 `SceneDraft`、GLB、`scene-package.json` 与 `JobReport` v0。它是 [大厂区 3D Tiles 产品规格与验收基线](../../大厂区3DTiles产品规格与验收基线.md) 的两层输入基础，不是完整大厂区 HLOD 产品方案。

实现位于 `SceneBuilder.Tiles`：生成器组合既有 `ScenePackageValidator` 与 `BinaryGlbValidator`，但不复制 GLB 二进制解析。Domain 不理解 3D Tiles，也不访问文件系统。现有外部 `ITilesConverter` POC 边界保持不变；本任务不选择外部转换器。

## 坐标、树和内容

Tileset 固定为 `asset.version="1.1"`、Local Cartesian、meters、Z-up。不会把 CAD 坐标解释为经纬度，不写 Root transform，也不实现 WGS84、ECEF、EPSG、`region` 或 `sphere` Bounding Volume。

树固定两级：Root 无 content，`refine="ADD"`，`geometricError` 等于请求策略的正有限 `RootGeometricErrorMeters`；Root children 为稳定排序后的分区叶子。每个有效分区恰好对应一个 leaf，leaf 的 `geometricError=0`、没有 children 或 refine，`content.uri` 直接采用经过场景包校验的相对 `artifactPath`。不转换 b3dm，不复制 GLB，也不实现 HLOD、LOD 或隐式分块。

常规叶子按 `XIndex`、`YIndex` 升序，Global 固定最后；不得依赖 JSON 输入顺序、字典或目录枚举。Global 与普通分区使用同一 leaf 结构，只出现一次。

## Bounds 和 geometric error

每个 leaf 只能使用分区 `ContentBounds`，绝不以 `CellBounds` 替代。`CadBounds` 必须是 `Computed`、有限且有序。轴对齐 3D Tiles Box 固定为：

```text
[centerX, centerY, centerZ,
 halfX, 0, 0,
 0, halfY, 0,
 0, 0, halfZ]
```

中心以 `min + (max - min) / 2` 计算，避免 `min + max` 溢出。任何退化轴仅扩大 culling envelope 的半长至正有限 `MinimumBoundingHalfExtentMeters`；不修改 ContentBounds、GLB 或真实几何。

Root Bounds 为所有实际进入 Tileset 的 ContentBounds 并集，而非未加验证的 `SceneBounds`。Validator 以有限容差验证每个 child Box 被 Root Box 包含。`RootGeometricErrorMeters` 仅是 Root 到叶子细化的明确选择参数，不代表 HLOD 误差。

## 输入筛选、状态与 URI

生成前必须由 `ScenePackageValidator` 验证输入。只允许已验证、带安全 `artifactPath` 的成功分区；失败、取消、超时或无路径分区绝不写入 Tileset。当前 SB-11B 发布的严格 Index 仅保留成功分区；契约仍为未来部分场景包兼容 `PartiallySucceeded + artifactPath`。若输入带被排除分区：`AllowPartialScenePackage=true` 才发布并返回 `PartiallySucceeded`，否则失败。没有有效分区时失败。

URI 不猜测文件名，直接使用 Index 的 artifactPath，并再次验证相对 `/` 路径、无 `..`、无反斜杠、无根路径、无 URI scheme、以 `.glb` 结尾且解析后仍在 package root。每个 URI 二次调用现有 GLB Validator，且同一 URI 最多验证一次。

## 严格 JSON、验证与发布

内部 DTO 使用 camelCase，所有集合非 null、所有数值有限。Tileset Validator 读取严格 JSON 并拒绝未知字段、错误版本、Root content、空 children、错误 refine、非法/重复 URI、非法轴对齐 box、非零 leaf error、Root 未包含 children、和与场景包成功分区不一致的内容。它再次验证引用 GLB。

输出固定为 `<scene-package-directory>/tileset.json`。默认拒绝已有文件和 `OverwriteExistingTileset=true`。生成器先在同目录创建唯一临时 JSON，写入后用 Validator 对临时文件验证，再原子移动到最终名称；失败或取消只删除其自身临时文件，绝不删除包目录、GLB 或 `scene-package.json`。

## 验证和非目标

单元/集成测试覆盖策略、普通/负/极端/退化 Bounds、Root 并集、稳定排序、URI 越界、部分包、GLB 二次验证、严格 JSON、原子发布、输入只读和取消。SmokeTest 新增 `--mode tileset`：真实 Blender 先生成 2 个常规分区和 1 个 Global GLB，再生成并验证含 3 个 leaf 的 tileset。

SB-12A 不实现地理配准、Root Transform、b3dm/i3dm、HLOD、LOD、模型简化、压缩、Metadata、Cesium/IDTS 前端、HTTP/任务接口或任何数据库与缓存。
