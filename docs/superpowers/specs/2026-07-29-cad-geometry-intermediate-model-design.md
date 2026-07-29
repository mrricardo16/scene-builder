# SB-05：CAD 基础几何中间模型与坐标标准化设计

## 目标与范围

本任务在 `SceneBuilder.Domain` 建立解析器无关的基础 CAD 几何契约和纯坐标标准化服务，在 `SceneBuilder.Cad` 建立 ACadSharp DXF 映射。它以既有 `CadDocumentModel` 作为结构摘要，不替代、不扩展 `JobReport` v0，也不向领域层泄漏 ACadSharp 类型。

本仓库当前没有 `SceneBuilder.Geometry` 项目。几何契约依赖既有 Domain 契约，适配器依赖 Domain，因此本次不新增项目：契约留在 Domain，DXF 映射和标准化实现留在 Cad，避免循环依赖和空项目。

支持的模型空间直接实体只有 `LINE`、`LWPOLYLINE`、`ARC`、`CIRCLE`、`INSERT`。Block 定义仍只以 `CadBlockModel` 摘要表达；不提取其内部完整几何，不展开 INSERT，不加载 Xref。墙体、地面、柱、规则匹配、SceneNode、SceneDraft 自动生成、Blender、GLB、3D Tiles、DWG、ezdxf、外部进程、HTTP API 和数据库均不在范围内。

## 领域契约

Domain 增加有限三维值对象 `CadPoint3`，以及 INSERT 缩放值对象 `CadScale3`。二者只接受有限数值，保留 record 值相等性；`CadScale3` 默认是 `(1, 1, 1)`，缩放值按 DXF 原样保存，不解释为长度单位。

几何基类 `CadGeometryEntity` 统一保存非负 `SourceOrder`、来源图层、规范化大写 `EntityType` 和既有三态 `CadBounds`。它不保存 ACadSharp 实体或文档。派生类型为：

- `CadLineGeometry`：起点和终点，保留三维坐标，范围与两点一致；
- `CadPolylineGeometry`：有序 `CadPolylineVertex` 集合、`IsClosed` 和顶点 bulge；不自动补首尾点，不离散 bulge；LWPOLYLINE 的统一 `Elevation` 写入每个顶点的 Z；
- `CadArcGeometry`：圆心、半径、起止角度（明确为 degrees）；不采样，不修改跨 0 度方向语义；
- `CadCircleGeometry`：圆心与半径；不转 polygon；
- `CadInsertGeometry`：Block 名、插入点、旋转角度和 X/Y/Z 缩放；不展开 Block，也不根据名称推断设备。

半径必须是有限正数，角度、bulge 与所有坐标必须有限。Normal/ExtrusionDirection 不进入本次基础几何契约；当前实现保留 ACadSharp 已给出的坐标值，不进行 OCS/WCS 重解释。

`CadGeometryDocument` 同时保留既有 `CadDocumentModel` 摘要、模型空间直接实体和诊断。`CadGeometryExtractionResult` 通过 `Succeeded`、`PartiallySucceeded`、`Failed` 明确读取结果：支持实体全部映射为成功；存在未支持或单个支持实体映射失败时为部分成功且保留其他实体；DXF 无法读取时为失败。

## ACadSharp 映射

`ACadSharpDxfGeometryExtractor` 只读取一次请求的 DXF，然后在内部 mapper 中从同一个 `CadDocument` 观察摘要和几何。`SourceOrder` 使用模型空间直接实体的零基读取顺序，不承诺跨文件稳定性。现有检查器与几何提取器复用同一内部文档 mapper，避免在同一调用路径中为摘要、几何和标准化重复解析文件。

映射时，`LINE` 读取 `StartPoint`、`EndPoint`；`LWPOLYLINE` 读取有序 `Vertices`、`Elevation`、`IsClosed` 和每个顶点的 `Bulge`；`ARC`/`CIRCLE` 读取圆心、半径和 ARC 的角度；`INSERT` 读取 Block 名、`InsertPoint`、`Rotation`、`XScale`、`YScale`、`ZScale`。ACadSharp 3.6.35 提供的 ARC 和 INSERT 角度为弧度，映射时转换为领域契约固定的 degrees。实体范围优先采用现有隔离的 `GetBoundingBox()` 观察值；支持实体映射异常或无效值会产生不含原始路径、坐标或第三方堆栈的 `GEOMETRY_ENTITY_MAPPING_FAILED`，不会把整个 DXF 读取伪装为 `DXF_PARSE_FAILED`。

未支持实体继续参与 `CadEntityTypeSummary` 和既有 `DXF_ENTITY_UNSUPPORTED` 诊断，但不进入几何实体集合。`ARC`、`CIRCLE`、`INSERT` 从本任务起属于受支持实体，不再触发该未支持诊断。

## 坐标标准化

`CadGeometryNormalizer` 独立于 ACadSharp，输入 `CadGeometryDocument`，输出新的 `NormalizedCadGeometryDocument`，绝不原地修改原始几何。正常结果含 `CadCoordinateContext`：来源单位、换算为米的比例和来源局部原点，以及独立的局部米制文档 `Bounds`。局部原点仅在文档范围为 `Computed` 时取 `MinX`、`MinY`、`MinZ`。

换算比例固定为：毫米 `0.001`、厘米 `0.01`、米 `1`、英寸 `0.0254`、英尺 `0.3048`。标准化公式是 `local = (source - origin) * unitScaleToMeters`；圆弧和圆的半径乘比例，角度、bulge、旋转角度和 INSERT 缩放保持原值。范围状态传播为 `Computed -> Computed`、`Empty -> Empty`、`NotEvaluated -> NotEvaluated`。

`Unknown` 或 `Unitless` 且存在几何时返回失败和 `GEOMETRY_UNIT_UNRESOLVED`，不猜测单位。存在几何而文档范围不是 `Computed` 时返回失败和 `GEOMETRY_BOUNDS_NOT_COMPUTED`，不以 `(0,0,0)` 代替原点。空文档可返回空成功结果；它不伪造局部原点或坐标上下文。

## 兼容性、诊断和公开边界

既有 `CadDocumentModel`、`CadBounds` 三态、Block/实体类型摘要、`CadInspectionResult`、DXF 诊断、DWG `UnsupportedDwgProbe`、DoctorReport 和 JobReport v0 JSON 均保持契约。新增内部几何文档不得直接作为公开报告序列化。

新增诊断仅使用大写 ASCII：`GEOMETRY_UNIT_UNRESOLVED`、`GEOMETRY_BOUNDS_NOT_COMPUTED`、`GEOMETRY_ENTITY_MAPPING_FAILED`。公开诊断不写入私有绝对路径、客户或设备信息、原始文字、完整 Block 名、完整坐标、ACadSharp 堆栈或内部类型细节。

## 验证策略

先运行失败测试，再写最小实现。Domain 覆盖值对象、实体不变量、空集合、单位比例、局部原点、范围状态传播和原始几何不被覆盖。CAD 覆盖公开合成 DXF 的五类受支持实体、模型空间隔离、稳定 SourceOrder、INSERT 变换、未支持实体部分成功和重复执行稳定性。测试只使用 `tests/fixtures/synthetic` 下公开合成样本。
