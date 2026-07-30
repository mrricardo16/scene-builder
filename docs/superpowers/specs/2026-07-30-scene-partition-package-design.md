# SB-11B：确定性空间分区与多 GLB 场景包设计

## 目标与边界

SB-11B 将既有 `SceneDraft` 按固定米制 XY 网格规划为多个非空分区，并组合现有 `IBlenderSceneGenerator` 生成多个独立 GLB 与版本化场景包索引。它不重新解析 DXF、不重新执行修复或分类、不修改输入 `SceneDraft`、源资产或 `JobReport` v0；不裁剪几何、不复制跨区对象，也不实现缓存、并行 Blender、LOD、3D Tiles 或 IDTS 接入。

`ScenePartitionPlanner` 是纯 Domain 内存计算：输入 `SceneDraft` 与不可变 `ScenePartitionPolicy`，输出稳定 `ScenePartitionPlan`。根层 `Pipeline` 从 Plan 派生只含归属对象的 `SceneDraft`，顺序调用既有 Blender 生成器，验证每个产物，并在受控 staging 中写入 Index 后原子发布场景包。Blender 不参与规划、Index 或发布；此放置避免 Application 与 Blender 产生引用环。

## 分区策略与网格

默认策略为 `CellSizeMeters=100`、原点 `(0,0)`、`MaximumIntersectedCellsPerObject=16`、大对象进入 `partition-global`、无效 Bounds 失败。Cell Size 必须有限且大于零；Origin 必须有限；阈值大于零；未知枚举、NaN 与无穷均拒绝。

网格只使用米制 XY：Cell `(ix, iy)` 为 `[originX+ix*size, originX+(ix+1)*size)` 与对应 Y 半开区间。点恰在右/上边界归入右/上 Cell；负数使用 `Floor`，因此 `-0.001` 属于 `-1`，`-100` 属于 `-1`。非零范围的最大边界以 `Math.BitDecrement(max)` 计算最后相交 Cell，避免把仅接触右/上边界的空 Cell 算入。零宽或零高 Bounds 仍覆盖一个 Cell。先安全计算四个索引和乘积，超过阈值时不枚举 Cell。

常规 ID 固定编码为 `partition-x-p000000-y-m000001`：`p` 表示非负、`m` 表示负，数值使用 Invariant Culture；Global ID 固定为 `partition-global`。常规分区按 X、Y 升序，Global 最后，不依赖输入、Dictionary 或 ID 字符串排序。

## 对象归属

每个 SemanticObject 必须与唯一 SceneNode 对应。Wall、Floor、Column、Road 以可信 Bounds 的稳定中心 `min + (max-min)/2` 为 Anchor。StaticFacility 与 DynamicEquipment 以匹配 Node 的有限 Transform Position 为 Anchor；不叠加 Bounds 中心、不按 Block 修正方向。资产 Anchor 在 Bounds 外发出脱敏稳定诊断；默认仍按策略处理，不篡改 Transform。

每个对象记录 `OwnerPartitionId`、稳定排序的 `IntersectedPartitionIds` 与 `CrossesPartitionBoundary`。普通跨区对象只进入 Anchor 分区，不复制不切割。覆盖 Cell 数超过阈值时按 `LargeSceneObjectBehavior`：Global、Anchor 或 Fail。Bounds/Anchor 无效按 `InvalidBoundsBehavior` Skip 或 Fail；Skip 产生部分成功诊断，Fail 不返回可执行 Plan。

## 派生 Draft 与生成

每个 Partition Draft 保持原 SemanticObject、SceneNode、Transform、GeometryDefaults 和 SourceDocument 实例语义，只筛选本分区对象；Id 为 `{draftId}:partition:{partitionId}`，仅内部使用而不作为文件路径。非空分区按 Plan 排序单线程生成，避免并发 Blender。每次调用复用 `IBlenderSceneGenerator` 和既有 Asset Context，输出仅写该包 staging 下的 `partitions/<partitionId>.glb`，随后以既有 `BinaryGlbValidator` 验证。

分区失败不会删除已验证的其他 staging 产物。取消停止后续分区且不发布；超时/失败按 `ContinueAfterPartitionFailure` 决定是否继续，`PublishPartialPackage` 决定是否发布仅含成功分区的 Index。失败分区永不拥有 artifactPath。

## Scene Package Index 与发布

Index `contractVersion="1.0"`、`unit="meters"`，只记录稳定分区 ID、网格范围、内容范围、状态、相对 artifactPath、程序化/静态/动态计数和动态节点索引。根对象、每个分区和每个动态节点的契约字段均为必填，即使数值为零或分区坐标为 `null` 也必须显式出现；解析器拒绝未知字段、空 ID、未知枚举、缺失范围/计数或无成功分区。不得含绝对路径、Layer、Block、AssetId、Manifest 或工具输出。动态节点索引仅包含成功发布分区的动态 Node Id、所属 Partition Id、Position、Rotation 与 Scale。

Pipeline 先创建唯一受控 staging sibling，拒绝不安全 PackageName、已有最终目录和 `OverwriteExistingPackage=true`。`ScenePackageValidator` 会从 staging 严格反序列化 Index 并逐个以 `BinaryGlbValidator` 验证所有被引用 GLB；只有该验证通过才将 staging 目录移动为最终目录。失败清理仅限该 staging 目录，绝不伪造成功包。

## 验证与复杂度

Domain 测试冻结边界、负坐标、最大边界、ID、对象唯一归属、跨区/Global、无效输入、派生 Draft 与确定性。Application 使用 Fake Blender 覆盖全成、分区失败、部分发布、取消、超时、非法 GLB、Index 与原子发布；Package Validator 复用 `BinaryGlbValidator`。SmokeTest 的 package 模式使用真实 Blender，生成至少两个常规分区和一个 Global 分区，验证每个 GLB、Index 和清理。

规划不创建空 Cell：对象索引和归属为 O(N)，排序 O(N log N)，Blender 为 P 次串行调用。SB-11B 不承诺增量重建或内容哈希复用。

## SB-12A 衔接

SB-12A 只读取已发布的 `scene-package.json` 与 `partitions/*.glb`，以 `ContentBounds`（而不是 `CellBounds`）构建 3D Tiles 1.1 Root + Leaf 索引。它不会重跑本设计中的 Planner、Draft Factory 或 Blender；失败分区没有 artifactPath，不能进入 Tileset。
