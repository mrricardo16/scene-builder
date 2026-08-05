# CORE-04A：版本化 Build Input Snapshot

## 范围

CORE-04A 将受控 DXF Analyze 升级为 Analysis v2，并同时发布版本化的 Build Input Snapshot v1。它不创建 SceneDraft，不执行 Build，也不启动 Blender、Scene Package 或 Tiles 组件。

## 契约与确定性

Analysis v2 保留 v1 字段并新增轻量 `buildInputSnapshot` 描述符。Snapshot 保存标准化 Domain 几何、轮廓、Repair 候选、可重分类 Subject、分析时分类和可确认的资产候选事实。所有对象 ID 从已有稳定 SourceOrder 或 Subject/Action ID 派生；ContentHash 是排除 SnapshotId 与自身 Hash 的 canonical UTF-8 JSON SHA-256，SnapshotId 为 `snapshot-` 加该 Hash。

Analysis v1 只有结构、计数、Bounds 和分类统计，不能作为后续 SceneDraft 的完整输入，也不能支持不重读 DXF 的重分类。Snapshot 与 Summary 分工：Summary 用于快速诊断和 CLI 摘要，Snapshot 用于可审计的 Build 输入版本化。当前 SceneDraftBuilder 所需的标准化几何、轮廓、单位/坐标上下文、修复候选和分类 Subject 由现有 Domain 模型映射保存；具体 CAD 库对象和原始文件不进入 Artifact。

## Snapshot v1 字段

- 顶层保存 `contractVersion`、`snapshotId`、`analysisId`、`sourceFingerprint`、坐标上下文、源/标准化 Bounds、诊断和以下稳定集合。
- `geometryObjects` 保存 GeometryObjectId、标准化 `CadGeometryEntity`、Layer、EntityType、SourceOrder 和 Bounds。
- `contours` 保存 ContourId、完整现有轮廓模型、GeometryObject 引用、闭合/方向/验证状态、Bounds 和诊断。
- `repairCandidates` 保存现有 RepairAction、稳定引用、算法参数和可应用状态；不自动应用或新增修复算法。
- `classificationSubjects` 保存规则重分类所需的现有 Layer、Block、EntityType、GeometryKind、几何/轮廓引用事实；Analyze-time Classification 单独保存，不当作最终冻结分类。
- `assetCandidates` 只保存 Analyze 已确认的候选类型、Subject 引用和位置/旋转/缩放事实，不猜测 GLB 路径、AssetId 或目录匹配。

所有集合序列化为非 null 数组。Geometry 只保存一次，其他集合使用 ID 引用，避免重复顶点数据。单位未知时保持 Unknown；标准化坐标使用现有 Analyze 坐标上下文，不应用 Plan 变换。

## ID、Hash 和数值

GeometryObjectId 使用稳定 SourceOrder 派生；ContourId、RepairActionId 和 Subject ID 复用现有稳定 Domain 标识；AssetCandidateId 从 Subject 标识派生。禁止随机 GUID、`GetHashCode()`、内存地址和未排序集合索引。相同输入、规则和选项必须产生相同 AnalysisId、SourceFingerprint、对象 ID、集合顺序、SnapshotId、ContentHash 和 JSON 字节。

Canonical payload 固定属性顺序、camelCase 枚举、集合顺序、UTF-8、缩进和有限数字格式，并排除 SnapshotId、ContentHash、绝对路径、临时目录、时间、机器名和进程 ID。0 与 -0 使用相同 canonical 表达；NaN、Infinity 和 -Infinity 被拒绝。Bounds 必须有限且 min 不大于 max。

## Serializer、Validator 和发布

`CadBuildInputSnapshotFactory` 从现有 Analyze 结果映射 Snapshot；`CadBuildInputSnapshotCanonicalHasher` 计算内容哈希；`CadBuildInputSnapshotValidator` 检查版本、必填标识、集合非 null、ID 唯一、引用存在、Bounds 和有限数值；`CadBuildInputSnapshotSerializer` 使用 UTF-8 Stream 写 staging，反序列化回读并重新校验 Hash 后原子移动。正式 Artifact 不覆盖已有文件，失败或取消清理 staging；Summary 只有在 Snapshot 发布成功后才发布，两个发布阶段失败时不保留半成品 Snapshot。

## 兼容、取消与隔离

Analysis v1 仍可读取，v1 的 Snapshot 描述为 Unavailable；Analysis v2 的 `buildInputSnapshot` 为 Available 时必须包含完整 descriptor。CORE-03 的 `plan create` 同时接受 v1/v2，但不静默升级 v1，也不改变 AnalysisId。Frozen Plan v1 继续由 Gate 拒绝，CORE-04B 才负责 Snapshot 绑定和 Build-ready 配置。

取消检查覆盖映射批次、Snapshot 写入前、严格回读前后、Summary 写入前和最终发布前；取消返回 Cancelled/退出码 3，不发布 Summary、Snapshot 或 Build Artifact。Snapshot 不启动 SceneDraftBuilder、Blender、ScenePackageGenerator、TilesetGenerator 或任何外部进程。当前 Analyze 仍按现有流水线在内存中持有几何；大文件峰值和规模基线留给 LARGE-00，不在本任务中宣称性能结论。

## 发布与兼容

Snapshot 先写入 `analysis/build-input-snapshot.json.staging`，严格 JSON 回读、校验后原子移动；摘要只在 Snapshot 发布成功后写入 `analysis/cad-analysis.json`。Analysis v1 读取路径保持不变，CORE-03 的 `plan create` 接受 v1 与 v2；Frozen Plan v1 未绑定 Snapshot，仍不具备 Build Ready 条件。

## 边界

不包含绝对路径、原始 DXF、ACadSharp 类型、随机 GUID、时间、机器或进程数据。未知单位不猜测；Repair 仅记录候选而不自动应用；Asset Candidate 不猜测 GLB 或 AssetId。

CORE-04A 不实现 Frozen Plan v2、SceneDraft Mapper、真实 Build Handler、build CLI、Blender 调用、GLB、Scene Package、3D Tiles、Avalonia、DWG、HLOD/LOD、Mesh 简化、纹理压缩、增量构建、Viewer 或 IDTS 集成。CORE-04B 的消费边界是读取并验证本 Snapshot 的 SnapshotId/ContentHash，再冻结完整语义配置；它不能回退到重新解析 CAD。
