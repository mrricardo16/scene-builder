# SB-09 厂房语义对象与 SceneDraft 设计

## 目标和边界

SB-09 将 SB-08 的可信 `CadClassificationResult` 与已经标准化的 `NormalizedCadGeometryDocument`、最终选定的 `CadContourDocument`、`CadDocumentModel` 连接为纯内存 `SceneDraft`。它生成可供后续三维建模消费的稳定语义对象和节点；不生成 Mesh、不调用 Blender、不写 GLB 或 3D Tiles，也不读取 DWG、Xref 或代理对象。

Domain 保存解析器无关的语义契约、来源索引和 `SceneDraftBuilder`。Application 只在需要时从 `CadGeometryRepairResult` 选择 `RepairedDocument` 或原始轮廓，再调用 Domain；它不重复规则匹配、轮廓验证或修复。`JobReport` v0、CLI、文件输出和公开 JSON 均不变。

## 数据流与职责

```text
CadClassificationResult + CadContourDocument + NormalizedCadGeometryDocument + CadDocumentModel
  -> SceneDraftBuilder (验证来源、兼容性、确定性)
  -> CadSemanticObject[]
  -> SceneNode[]
  -> SceneDraft
```

分类不是语义对象：构建器必须先以 `Subject.Id` 找到实际 Contour、OpenSegment 或 Insert，核对 `Subject.Kind`、Bounds、有效性和分类兼容性。来源缺失、伪造 Subject、重复结果或不兼容类型都只跳过该对象并诊断；核心索引出现重复稳定 Id、无效 DraftId 或失败的分类结果才使整个草稿失败。构建器不修改任一输入集合或对象。

`SceneDraft` 直接保存 `SemanticObjects` 与 `Nodes`，不会再增加与其重复保存完整对象的 `CadSemanticScene`。`SceneNode` 是消费语义对象的稳定呈现描述，不承担分类、规则或三维网格职责。

## 语义对象和来源兼容性

| 分类 | Contour | OpenSegment | Insert | 语义几何 |
| --- | --- | --- | --- | --- |
| Wall | 允许且有效 | 允许 | 禁止 | 有效闭合 Profile 或有效 Baseline |
| Floor | 允许且有效 | 禁止 | 禁止 | 有效闭合 Profile |
| Column | 允许且有效 | 禁止 | 禁止 | 有效闭合 Profile，含 Circle |
| Road | 允许且有效 | 允许 | 禁止 | Area 或 Centerline |
| StaticFacility | 禁止 | 禁止 | 允许 | 实际 Insert 引用 |
| DynamicEquipment | 禁止 | 禁止 | 允许 | 实际 Insert 引用 |
| Unclassified | 不生成 | 不生成 | 不生成 | 无 |

Wall 的 Contour 表示 `ClosedProfile`，OpenSegment 表示无厚度的 `Baseline`；Road 的两种来源分别是 `Area` 与 `Centerline`。Floor 和 Column 均不能由散段或 Insert 推断。设施对象只读取实际 `CadInsertGeometry` 的标准化 Position、RotationDegrees、Scale 和 BlockName；不使用 `CadBlockModel`，不展开 Block、不查询资产文件、不把 Block 名作为业务主键。

每个 `CadSemanticObject` 都有稳定 Id、`SourceSubjectId`、`SourceSubjectKind`、固定 Classification、可信 Bounds 和只读传递的 `CadRuleGeometryDefaults`。Id 固定为 `semantic:{classification}:{subjectId}`；Node Id 固定为 `node:{semanticObjectId}`。这些标识不含图层、Block、路径、坐标或随机 GUID，也不承诺跨文件永久全局唯一。

## GeometryDefaults 和高度

当前规则契约仅有 `HeightMeters`。Wall 和 Column 将其传递为同名可选字段；Floor 不将它解释为厚度，Road 不将它解释为宽度，设施不将它解释为缩放或资产信息。缺失高度不会补为经验值：对象和节点仍可生成，`HeightMeters` 为 `null`，并增加不泄漏来源名称的 `SCENE_GEOMETRY_DEFAULT_MISSING` 警告，使结果为 `PartiallySucceeded`。非有限或非正高度不能成为可建模高度，亦以该对象的非阻塞诊断处理；SB-08 的规则验证仍负责拒绝非有限配置。

## SceneNode 和 SceneDraft

`SceneNodeContentKind` 明确区分 `ProceduralStaticGeometry`（Wall、Floor、Column、Road）、`StaticAssetReference`（StaticFacility）和 `DynamicAssetReference`（DynamicEquipment）。每个节点含语义对象 Id、分类、来源 Subject Id/Kind、可信 Bounds、只读 GeometryDefaults 和脱敏合成 Name。只有两个资产引用类型具有 Insert Transform；程序化节点不伪造 Transform。

`SceneDraftBuildRequest` 要求由调用方明确给出非空 DraftId 和四个已建立的输入。结果的 `Succeeded` 表示所有非未分类结果均已转换；`PartiallySucceeded` 表示保留至少一个合法对象而跳过一个或多个分类对象，或存在缺失高度；`Failed` 表示分类失败、DraftId 无效、来源索引重复或其他核心输入不可信。Unclassified 是正常输入，不生成对象、节点或额外失败。

语义对象、节点、跳过 Subject 和诊断都用 `StringComparer.Ordinal` 稳定排序。来源索引也使用 Ordinal，并拒绝重复 Contour、Segment、Insert、分类 Subject 或输出 Id，绝不选择“第一个”。构建复杂度为来源索引 O(G)、分类转换 O(C)、输出排序 O(C log C)。

## 诊断、隐私与兼容性

新增诊断只使用全大写 ASCII 稳定代码，并使用匿名 Subject Id：`SCENE_DRAFT_INPUT_INVALID`、`SCENE_SOURCE_SUBJECT_NOT_FOUND`、`SCENE_CLASSIFICATION_SOURCE_MISMATCH`、`SCENE_SEMANTIC_SUBJECT_INCOMPATIBLE`、`SCENE_GEOMETRY_DEFAULT_MISSING` 和 `SCENE_DUPLICATE_SUBJECT_RESULT`。说明文本不含绝对路径、真实图层或 Block 名、客户名称、设备编码、完整坐标、规则 JSON、ACadSharp 类型或堆栈。

此阶段维持 `CadBounds` 三态、CAD/轮廓/修复/规则契约、DXF 与 DWG 状态、DoctorReport、Tiles 适配器和 `JobReport` v0 JSON。内部 SceneDraft 绝不直接序列化为公开产物；公开脱敏适配器、Mesh、Blender、GLB、分区与 Tiles 都留给后续门禁任务。

## 验证

先以 Domain 失败测试定义语义对象不变量、兼容矩阵、伪造来源、重复 Id、稳定排序和原始输入不变，再实现最小构建器。Application 测试仅覆盖修复结果的轮廓选择；Cad 集成测试使用公开合成 DXF 串联提取、标准化、轮廓、规则、语义和草稿。完成时验证全量测试、0 警告构建、严格 UTF-8、严格 JSON、隐私/边界扫描、JobReport v0 和独立只读复审。
