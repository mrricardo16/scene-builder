# CadDocumentModel 结构摘要扩展设计与实现记录

## 决策与范围

本设计已于 2026-07-28 由项目负责人确认并按本记录实现。目标是在不接入 ezdxf、不改变 DWG 支持状态、不启动外部进程的前提下，补足 `CadDocumentModel` 对范围状态、Block 定义摘要和模型空间实体类型摘要的稳定表达能力。

本次仅修改 `SceneBuilder.Domain`、现有 ACadSharp DXF 适配器、公开合成 fixture、单元测试及契约文档。ezdxf 仍是独立 POC 候选，DWG 仍返回 `DWG_UNSUPPORTED`。

## 领域模型

### 范围状态和不变量

新增 `CadBoundsState`：

| 值 | 含义 |
| --- | --- |
| `NotEvaluated` | 当前步骤未能完整且安全地取得范围；六个坐标不得被解释为有效范围。 |
| `Empty` | 已计算范围，但输入集合为空；六个坐标均为 0。 |
| `Computed` | 已得到完整有效范围；六个坐标均为有限数，且每一轴满足 `Min <= Max`。 |

`CadBounds` 保留六参数公共构造和 value-object 相等性，但六参数构造必须表示 `Computed`。`CadBounds.Computed(...)` 使用同一验证逻辑。`CadBounds.Empty`、`CadBounds.NotEvaluated` 和 `CadBounds.Computed(0, 0, 0, 0, 0, 0)` 状态不同，因此彼此不相等。范围对象不提供可绕过验证的公开 `init` 状态入口；NaN、正负无穷和反向范围必须抛出明确的参数异常。

`CadDocumentModel`、`CadLayerModel`、`CadBlockModel` 和 `SceneNode` 的默认范围均为 `CadBounds.NotEvaluated`。适配器只在完整计算成功后给出 `Computed`；输入集合为空给出 `Empty`；任何必要实体范围不可用则给出 `NotEvaluated`，不得以剩余实体伪造部分 `Computed`。

### Block 和实体类型摘要

新增不可变 `CadBlockModel`：

- `Name`：源 Block 名，供内存内规则匹配；公开报告不得输出该值。
- `EntityCount`：Block 定义内直接子实体数量，不能为负。
- `Bounds`：Block 本地坐标空间范围，不应用模型空间 INSERT 的平移、旋转或缩放。

新增不可变 `CadEntityTypeSummary`：

- `Type`：规范化的大写 DXF 实体类型名，不能为空或空白。
- `EntityCount`：模型空间该实体类型数量，不能为负。

`CadDocumentModel` 新增 `Blocks` 与 `EntityTypes` 两个 `IReadOnlyList<T>` 属性，默认均为 `Array.Empty<T>()`。它们是非 null 的只读摘要集合，不引入 `System.Collections.Immutable`，不保存原始实体、文字、坐标负载或第三方解析器类型。

## ACadSharp 映射策略

1. 对每个模型空间实体只观察一次：读取图层、规范 DXF 类型名和范围评估，形成私有快照；文档范围、图层范围、实体类型摘要和未支持实体诊断都从快照聚合，避免重复调用 `GetBoundingBox()`。
2. 模型空间实体按 `StringComparer.Ordinal` 分组和排序；`EntityTypes` 只统计模型空间直接实体，不统计 Block 定义内部实体。
3. `Blocks` 只表示普通 Block 定义，按 `Name` 使用 `StringComparer.Ordinal` 排序；不递归展开嵌套 Block，不加载 Xref，不包含 Model Space/Paper Space 内部 Block，也不保存 INSERT 实例。必须先使用当前 ACadSharp 的可验证 API 判断空间和 Xref 语义；若该版本缺少 API，名称判断只能封装为单独方法、由测试覆盖并在实现文档中声明为 POC 限制。
4. Block 的 `EntityCount` 仅统计直接子实体。空 Block 为 `Empty`；全部直接实体范围有效为 `Computed`；任一直接实体范围异常、非有限、反向或不可完整确认时为 `NotEvaluated`。
5. 单个实体或 Block 范围异常不改变成功的 DXF 读取为 `DXF_PARSE_FAILED`，对应范围降级为 `NotEvaluated`。保持现有 `DXF_DOCUMENT_EMPTY`、`DXF_UNIT_UNKNOWN`、`DXF_ENTITY_UNSUPPORTED` 代码及严重级别；本次不新增范围、Xref 或代理对象的产品诊断。

## 兼容性与非目标

- 此次只新增领域成员和显式范围语义；既有默认构造和 `CadBounds.Empty` 仍可使用。
- 不修改 `JobReport`、`SceneDiagnostic`、`JobArtifact`、`DoctorReport`、`CadInspectionResult` 或 `UnsupportedDwgProbe` 的外部行为和 JSON wire shape。
- 不实现 ezdxf/Python/AutoCAD/FreeCAD/Blender 调用、DWG 读取、Xref 内容加载、代理对象转换、几何标准化、规则引擎、SceneNode 自动生成、HTTP API 或数据库。
- `CadDocumentModel` 仍不是完整原始 CAD 实体存储；后续几何标准化另行定义几何中间模型。

## 测试与验收

先写并运行失败测试，再写最小实现。测试只使用公开合成数据：

1. Domain：验证三种范围状态、相等性、六参数构造和工厂验证、所有 NaN/无穷/反向轴拒绝、默认范围、默认非 null 空集合，以及 Block/实体类型摘要不变量。
2. CAD：新增 `public-synthetic-block-summary.dxf`，含普通 Block、空普通 Block、Block 内 `LINE`、模型空间 `INSERT`、`LINE` 或 `LWPOLYLINE`，以及至少两种模型空间实体类型。验证普通 Block 摘要、空 Block、稳定排序、模型空间 `INSERT`、实体类型隔离和范围状态。
3. 范围失败隔离：`CadBoundsAggregator` 是最小 internal 纯聚合边界，已由友元测试程序集验证：单个实体范围不可用时聚合为 `NotEvaluated`，不会生成部分 `Computed` 或误报 `DXF_PARSE_FAILED`；不得引入私有图纸、D 盘文件、外部进程或大型第三方类型 Mock。
4. 回归：现有空文档、闭合 polyline、未映射 circle、缺失/损坏/取消行为不变；`JobReport` v0 JSON 属性仍仅为 `jobId`、`createdAt`、`status`、`diagnostics`、`artifacts`。

## 文档同步

实现完成后同步更新本设计、`SB-03-DXF检查`、`场景草稿契约` 和 `规则配置预案`。规则中的 `entityTypes` 使用规范化大写 DXF 名称，例如 `LINE` 和 `LWPOLYLINE`，不依赖具体解析器 CLR 类型。文档必须明确：Block/实体类型/范围状态摘要已补足，但 ezdxf 未接入、DWG 仍不支持、Xref 内容保真和完整几何实体存储仍未实现。
