# SB-03：DXF 检查门禁

## 目标

将 DXF 建立为首条可验收输入路径，并以 `CadDocumentModel` 提供稳定、脱敏的结构摘要；它不是完整 CAD 几何模型或公开报告格式。

## 通过闸门

- `IDxfInspector` 的请求、结果和取消边界可由实现替换。
- 公开合成 DXF 覆盖单位、范围、图层、模型空间实体类型、Block 定义摘要和诊断；私有图纸只可在私有环境验证，不能提交到公开仓库。
- 解析失败返回可读诊断，不能伪造 `CadDocumentModel` 成功结果。
- 同一输入的摘要排序和计数可重复；任何必要范围不可完整计算时，范围状态必须是 `NotEvaluated`，不得生成部分范围。

## 当前状态

- 已完成 ACadSharp 3.6.35 的最小 DXF 检查实现：公开合成 ASCII DXF 可映射为 `CadDocumentModel`，覆盖线段、空文档、闭合 `LWPOLYLINE`、无单位、未映射 `CIRCLE` 和普通 Block 摘要；缺失与损坏输入返回稳定诊断。
- `CadBoundsState` 已区分 `NotEvaluated`、`Empty` 和 `Computed`。所有模型空间实体先各自观察一次，再从快照聚合文档范围、图层范围、实体类型和未支持实体诊断；范围异常只降级该聚合范围，不把成功读取改写为 `DXF_PARSE_FAILED`。
- `CadDocumentModel` 已包含非 null 的 `Blocks` 与 `EntityTypes` 只读摘要。实体类型只统计模型空间直接实体，使用大写 DXF 名称并按 Ordinal 排序；普通 Block 只统计直接子实体和本地范围，不递归、不中转 INSERT 变换、不加载 Xref。
- Block 过滤使用 ACadSharp 的 `BlockRecord.Layout` 与 `BlockTypeFlags.XRef`/`XRefOverlay`，不依赖名称回退规则。公开合成样本已验证普通 Block、空 Block、模型空间 `INSERT`、排序和连续执行稳定性。
- 在 SB-05 基础几何门禁中，公开合成 DXF 已可提取模型空间直接 `LINE`、`LWPOLYLINE`、`ARC`、`CIRCLE` 和 `INSERT`，并可按来源单位转换为局部米制坐标；这只是基础几何数据，不进行 Block 展开或厂房语义识别。
- 当前决定仍为 `continue-validation`，不是完整 CAD 支持承诺。ezdxf 未接入；DWG 自动读取仍不支持；Xref 内容加载/保真、代理对象语义保真、完整 Block 几何实体存储、长时间读取的底层可中断取消和资源限制仍待后续门禁验证。
- ACadSharp 的 `DxfReader.Read` 为同步读取。检查器在读取前和读取返回后检查 `CancellationToken`；底层读取开始后不能由当前适配器中断，取消会在读取返回后被观察并抛出。

## 追溯

| 输入 | 稳定契约 | 测试 | 产物 | 证据 |
| --- | --- | --- | --- | --- |
| 公开合成 DXF | `IDxfInspector`、`CadDocumentModel`、范围状态、诊断 | Golden、空/损坏/闭合轮廓/未知单位/未映射实体/Block 摘要/重复执行 | 检查结果、草稿候选 | `tests/fixtures/synthetic` 与 `SceneBuilder.Cad.Tests` |
| 私有匿名样本 | 同上 | 私有 POC | 私有检查记录 | 私有存储；不得提交图纸、路径、客户或设备信息 |
