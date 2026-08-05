# CORE-04B：Build-Ready Frozen Plan v2 设计

## 1. 现状与职责

Frozen Plan v1 仅保存 Draft 副本和截断内容标识。它没有绑定 Analysis v2 的 Snapshot descriptor，也没有保存可重分类的 RuleSet、资产内容身份、完整分区参数或 Tiles 参数，因此永远返回 `FROZEN_PLAN_NOT_BUILD_READY` 和 `PLAN_REFREEZE_REQUIRED`。

Frozen Plan v2 是 CORE-04C 的唯一语义输入。它只表示输入数据和配置已经完整、确定且可验证，不创建 SceneDraft，不启动 Blender，不生成 GLB、Scene Package 或 3D Tiles。`BUILD_READY_FROZEN_PLAN` 可以为 Available，而 `BUILD`、`BUILD_GLB`、`BUILD_SCENE_PACKAGE`、`BUILD_3D_TILES` 继续为 Planned。

## 2. 版本与生命周期

- Analysis v1 继续创建 Draft v1；Draft、Validation、Frozen Plan v1 均可严格读取，不静默升级或覆盖。
- Analysis v2 且 Snapshot v1 descriptor 为 Available 时创建 Draft v2。
- Draft v2 保存 Snapshot binding 和所有可编辑语义；Validation v2 绑定 Draft、Analysis、Snapshot 和资源内容；Freeze v2 重验全部绑定，展开默认值并发布 Frozen Plan v2。
- 相同 revision 的重复 Freeze 只接受字节相同的现有 Artifact；不同 revision 使用独立文件。

流程为：Analysis v2 + Snapshot v1 → Draft v2 → Validation v2 → Frozen Plan v2 → Build Readiness Validator。

## 3. Analysis 与 Snapshot 绑定

绑定保存 Analysis contractVersion、AnalysisId、SourceFingerprint、固定相对路径 `analysis/cad-analysis.json`，以及 Snapshot contractVersion、SnapshotId、ContentHash、固定相对路径 `analysis/build-input-snapshot.json`。读取时拒绝 URL、UNC、绝对 Artifact 引用、路径逃逸、reparse point、不支持版本、损坏 JSON、descriptor unavailable、ID 或 hash 不一致。

Validation 和 Freeze 都严格回读 Analysis/Snapshot，并重新计算 Snapshot ContentHash。只保存 SnapshotId 或只保存文件名都不构成有效绑定。

## 4. Draft v2 与 Validation v2

Draft v2 在现有单位、Z Offset、Yaw、Wall/Column 高度、Repair 和输出意图上增加 BuildInput、局部原点数值、规范化 RuleSet 快照、显式资产配置、分区配置和 Tiles 配置。任何修改通过 Save Revision 发布新 revision，并重算 DraftContentHash；内部 hash 不接受调用方伪造。

Validation v2 保存 PlanId、Revision、DraftContentHash、AnalysisId、SnapshotId、SnapshotContentHash、状态、稳定排序诊断和 ValidationContentHash。它验证有限数值、单位确认、局部原点、Yaw、Repair 引用、规则契约及冲突、几何高度、资产候选显式绑定、资源路径/hash/大小、输出依赖、分区和 Tiles 参数。Invalid Artifact 可以发布，但不能 Freeze。

## 5. 完整冻结配置

Frozen Build Configuration 由以下部分组成：

- InputInterpretation：来源单位、确认后的目标米制、单位来源、局部原点策略和有限 XYZ、Z Offset、规范化 Yaw、Local Cartesian / meters / Z-up。
- Geometry：仅现有 WallHeightMeters、ColumnHeightMeters；不增加厚度、道路宽度、门窗、屋顶或 LOD/HLOD。
- Repair：配置版本、Snapshot 中存在且可用的启用 action 快照；Freeze 不应用 Repair。
- Classification：规范化 `CadRuleSet` 正文、contractVersion 和 SHA-256。无规则且 Snapshot 没有分析时分类时允许空规则；已有分类而无规则正文时失败，不能复用分析时分类冒充重分类。
- Assets：CandidateId → AssetId 显式绑定、受控相对 GLB 路径、SHA-256、大小、Snapshot 提供的 transform 和缺失策略。禁止 BlockName 猜测、目录扫描或 Build 时读取外部原始路径。
- Outputs：三个发布选择，以及 3D Tiles 对内部 Scene Package 的依赖是否展开。
- Partition：当前 `ScenePartitionPolicy` 和 `ScenePackagePublicationPolicy` 的全部语义值。
- ThreeDTiles：当前 Root+Leaves 生成器的 geometric error、最小 Bounds 半径、partial policy、ADD、本地 Cartesian、meters、Z-up 和分区 GLB URI 策略。

Blender 路径、超时、日志级别、临时目录和并发属于未来运行配置，绝不进入 Frozen Plan。

## 6. 版本化默认值

`ConversionPlanDefaultProfileV2` 是 Freeze 前唯一默认来源。它显式给出 Single GLB 输出、Wall/Column 高度、100m 网格、原点 0/0、每对象最多 16 个网格、Global 大对象策略、严格无效 Bounds、非部分发布、Tiles root geometric error 100m、最小半径 0.001m、ADD 和 Root+Partition Leaves。Frozen Plan 保存所有展开值，而不是只保存 profile code；未来默认变化不影响已发布 Artifact。

## 7. RuleSet 与资产资源

RuleSet 采用 Frozen Plan 内嵌规范化快照，hash 覆盖完整正文，不依赖外部规则文件。资产文件由 `PlanAssetResourceImporter` 在 Freeze 前导入 `plans/resources/assets/<sha256>/asset.glb`：预检大小、安全打开、拒绝 reparse point、执行与现有 GLB 契约一致的严格结构校验、复制到 staging、严格回读/hash 后无覆盖原子发布。Draft/Frozen 只引用受控相对路径。

Snapshot 无资产候选时空资产配置合法；存在候选时每个候选必须有且仅有一个显式绑定，AssetId 必须存在。缺失、重复、未知、路径逃逸、hash/大小不匹配均使 Validation/Readiness 失败。

## 8. FrozenPlanId、序列化与不可变性

FrozenPlanContentHash 是排除 `FrozenPlanId` 和自身 hash 后 canonical UTF-8 JSON 的 SHA-256；`FrozenPlanId = "frozen-plan-" + FrozenPlanContentHash`。Hash 覆盖 identity、Build Input、RuleSet、资产、输出、分区和 Tiles 配置，不含时间、随机 GUID、绝对路径、机器或当前目录。

Frozen Plan v2 不保存可变 Draft。Factory 对集合和嵌套记录做深复制；Serializer 使用未知字段拒绝、严格 UTF-8、staging、严格回读、hash 重算和原子发布。修改 Draft、默认 provider 或外部原始资源不会改变已冻结 JSON。

## 9. Build Readiness

Readiness Validator 区分：

- v1：`FROZEN_PLAN_NOT_BUILD_READY`、`PLAN_REFREEZE_REQUIRED`；
- v2 Snapshot 缺失：`FROZEN_PLAN_BUILD_SNAPSHOT_MISSING`；
- v2 Snapshot/Analysis 不匹配：`FROZEN_PLAN_BUILD_SNAPSHOT_MISMATCH`；
- 配置或资源不完整：`FROZEN_PLAN_BUILD_CONFIGURATION_MISSING`；
- 所有契约、hash、绑定、资源和配置完整：`Ready`。

Readiness 只做读取和验证，不调用 SceneDraftBuilder、IBlenderSceneGenerator、ScenePackageGenerator、TilesetGenerator 或外部进程。现有 Build Handler 即使遇到 Ready v2 也不会执行生成；真实调度属于 CORE-04C。

## 10. Composition、CLI、进度与取消

Default profile、binding validator、configuration resolver、RuleSet snapshotter、asset importer、Frozen v2 factory/serializer 和 Readiness Validator 都由共享 Composition 构造并从 Host 暴露。Host 创建不读写 Plan、不复制资源、不启动进程；Program.cs 不手工拼装对象图。

保留 `plan create|validate|freeze`，不增加 `build`/`convert`。摘要只输出版本、Snapshot 是否绑定、Readiness 和相对 Artifact；不输出规则正文、资产原路径、Layer/Block 或 Snapshot 内容。进度仅使用 Plan 阶段和附件指定的 `PLAN_*` stage。各 IO、hash、规则/资产校验、Freeze 写入/回读/发布前后检查取消；取消返回 3，不发布半成品。

## 11. CORE-04C 消费边界与非目标

CORE-04C 只能消费通过 Readiness 的 Frozen Plan v2 和受控资源，不补业务默认值、不重新读取 DXF/规则原文件/资产原文件、不猜绑定。本任务不实现 SceneDraft Build、真实 Build Handler、Blender、GLB、Scene Package、3D Tiles、build CLI、Avalonia、DWG、Viewer、IDTS、HLOD/LOD、优化、缓存或大厂区性能验收。
