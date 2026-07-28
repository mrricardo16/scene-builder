# CadDocumentModel 结构摘要扩展设计

## 决策与范围

本设计已于 2026-07-28 由项目负责人选择。目标是在不接入 ezdxf、不改变 DWG 支持状态、不启动外部进程的前提下，补足 `CadDocumentModel` 对 Block、实体类型摘要和范围状态的稳定表达能力。

本次只修改 `SceneBuilder.Domain` 契约、现有 ACadSharp DXF 适配器、对应单元测试和契约文档。ezdxf 仍是独立 POC 候选，DWG 仍返回 `DWG_UNSUPPORTED`。

## 领域模型

### 范围状态

新增 `CadBoundsState`：

| 值 | 含义 |
| --- | --- |
| `NotEvaluated` | 当前步骤未计算范围，六个坐标不得被解释为有效几何范围。 |
| `Empty` | 已完成范围计算，但输入集合没有实体；六个坐标均为 0。 |
| `Computed` | 已计算出有效范围；六个坐标均为有限数，且 `Min <= Max`。 |

`CadBounds` 保留六个坐标和 `Empty` 静态值，同时新增状态属性、`NotEvaluated` 静态值及只接受有限且有序坐标的 `Computed(...)` 工厂方法。`CadDocumentModel`、`CadLayerModel` 和 `SceneNode` 的默认范围改为 `NotEvaluated`。空文档或空图层在适配器实际完成计算后使用 `Empty`；未支持的范围计算必须使用 `NotEvaluated`，不得伪造 `Empty` 或零坐标范围。

### Block 与实体类型摘要

新增不可变 `CadBlockModel`：

- `Name`：源 Block 名，供内存内规则匹配使用；公开报告不得写入该值。
- `EntityCount`：Block 定义内的实体数量。
- `Bounds`：使用上述显式状态。

新增不可变 `CadEntityTypeSummary`：

- `Type`：稳定 DXF 实体类型名称。
- `EntityCount`：模型空间中该类型的实体数量。

`CadDocumentModel` 新增 `Blocks` 与 `EntityTypes` 两个只读集合，默认均为空。它们是摘要，不保存原始实体、文字、坐标负载或第三方解析器类型。

## 映射与诊断语义

现有 `ACadSharpDxfInspector` 在其当前公开合成 DXF 能力范围内执行如下映射：

1. 模型空间实体按图层生成 `Layers`，按 CLR/DXF 稳定实体类型名生成 `EntityTypes`。
2. Block 定义生成 `Blocks`；Block 范围只在适配器能安全取得有限范围时写为 `Computed`，否则写为 `NotEvaluated`。
3. 文档和图层范围在确实遍历并得到有限范围时写为 `Computed`；已遍历但实体集合为空时写为 `Empty`。
4. 保持现有 `DXF_DOCUMENT_EMPTY`、`DXF_UNIT_UNKNOWN` 和 `DXF_ENTITY_UNSUPPORTED` 行为。此次不新增“范围未计算”或 Xref 产品诊断，以免把 POC 观察误报成已支持能力。

Block 名和实体摘要只用于领域内存模型及私有证据；任何未来 CLI/报告适配器必须遵守公开仓库脱敏规则。

## 兼容性与非目标

- 这是只新增成员和显式状态的契约扩展；既有默认构造、集合默认值与 `CadBounds.Empty` 保持可用。
- 不改变 `JobReport` 或其 JSON wire shape，也不实现 `conversion-report.json` v1。
- 不实现 Xref 内容加载、代理对象语义转换、范围递归修复、ezdxf 进程桥接或 DWG 自动输入。
- `CadDocumentModel` 仍不是完整原始 CAD 实体存储；几何标准化阶段另行定义所需的轮廓/几何中间模型。

## 验收与测试

先写并运行失败测试，再写最小实现。验收包括：

1. `CadBounds` 默认、空、未计算和已计算状态；非有限或反向范围被拒绝。
2. `CadDocumentModel` 的 `Blocks`、`EntityTypes` 与范围默认值保持不可变空集合和 `NotEvaluated`。
3. ACadSharp 的公开合成样本验证文档/图层范围状态、Block 摘要和实体类型摘要；空文档产生 `Empty`，未计算范围不伪装为 `Empty`。
4. 全部既有 Domain、CAD、Application 测试通过；新增测试不依赖私有图纸、D 盘工具或外部进程。

## 风险控制

该扩展不会将 ezdxf、AutoCAD 或 ACadSharp 类型泄漏进 Domain。若 ACadSharp 对某个 Block 或实体的范围计算抛出异常，该单项范围降级为 `NotEvaluated`，而非导致整个检查结果崩溃或生成虚假的成功范围。
