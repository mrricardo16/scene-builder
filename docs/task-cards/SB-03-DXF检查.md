# SB-03：DXF 检查闸门

## 目标

将 DXF 建立为首条可验收输入路径。

## 通过闸门

- `IDxfInspector` 的请求、结果和取消边界可由实现替换。
- 使用真实 DXF 样本完成单位、范围、图层、实体和诊断的验证。
- 解析失败返回可读诊断，不能伪造 `CadDocumentModel` 成功结果。

## 当前状态

- 已完成 ACadSharp 3.6.35 的最小 DXF 检查 POC：公开合成 ASCII DXF 可映射为 `CadDocumentModel`，覆盖线段、空文档、闭合 `LWPOLYLINE`、无单位和未映射 `CIRCLE`；缺失与损坏输入返回稳定诊断。
- ezdxf 1.4.4 已在隔离 Python 环境成功读取两份私有 `AC1032` DXF，并枚举图层、Block 和模型空间实体；它仍是未接入产品的候选。两个样本的插入单位分别为未声明和毫米，映射 POC 对应产出 `CadUnit.Unitless`/`DXF_UNIT_UNKNOWN` 与 `CadUnit.Millimeters`；不擅自推断未声明单位。
- 在固定私有样本上连续执行 10 次 ezdxf 读取，均成功且 DXF 版本、单位、图层/Block/实体及实体类型摘要一致；各次耗时只保存在私有证据中，不能作为生产性能承诺。
- 两个私有样本均未检测到 Xref 定义或模型空间 Xref 引用，因此不能把“未发现 Xref”表述为 Xref 保真；后续必须使用受控 Xref 样本单独验证。第一个样本含代理实体，ezdxf 可只读提取其代理图形且未出现提取失败，但原始对象语义仍未证明可保留。
- 映射 POC 仅输出脱敏样本标识、单位、图层/实体摘要和稳定摘要，不落盘图层名、Block 名或绝对路径。它确认当前 `CadDocumentModel` 缺少 Block 目录，且不能区分“尚未计算范围”与空范围；因此映射状态只能是 `Partial`，不能伪造完整领域文档。
- 当前决定为 `continue-validation`，不是已接受依赖或正式 DXF 支持；仍需受控 Xref 样本、代理对象语义保真、实体覆盖、取消/资源限制以及上述契约缺口的设计决策和运行环境证据。
- ACadSharp 的 `DxfReader.Read` 为同步读取。检查器在读取前和读取返回后检查 `CancellationToken`；底层读取开始后不能由当前适配器中断，取消会在读取返回后被观察并抛出。

## 追溯

| 输入 | 稳定契约 | 测试 | 产物 | 证据 |
| --- | --- | --- | --- | --- |
| 合成 DXF 或私有匿名样本 | `IDxfInspector`、`CadDocumentModel`、诊断 | Golden、空/损坏/闭合轮廓/未知单位/未映射实体 | 检查报告、草稿候选 | 私有 POC 记录；见 [样本规范](../样本与黄金结果规范.md) |
