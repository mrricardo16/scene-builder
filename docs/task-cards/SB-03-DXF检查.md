# SB-03：DXF 检查闸门

## 目标

将 DXF 建立为首条可验收输入路径。

## 通过闸门

- `IDxfInspector` 的请求、结果和取消边界可由实现替换。
- 使用真实 DXF 样本完成单位、范围、图层、实体和诊断的验证。
- 解析失败返回可读诊断，不能伪造 `CadDocumentModel` 成功结果。

## 当前状态

- 已完成 ACadSharp 3.6.35 的最小 DXF 检查 POC：公开合成 ASCII DXF 可映射为 `CadDocumentModel`，覆盖线段、空文档、闭合 `LWPOLYLINE`、无单位和未映射 `CIRCLE`；缺失与损坏输入返回稳定诊断。
- 当前决定为 `continue-validation`，不是已接受依赖或正式 DXF 支持；仍需私有脱敏真实样本、实体覆盖、重复性和运行环境证据。
- ACadSharp 的 `DxfReader.Read` 为同步读取。检查器在读取前和读取返回后检查 `CancellationToken`；底层读取开始后不能由当前适配器中断，取消会在读取返回后被观察并抛出。

## 追溯

| 输入 | 稳定契约 | 测试 | 产物 | 证据 |
| --- | --- | --- | --- | --- |
| 合成 DXF 或私有匿名样本 | `IDxfInspector`、`CadDocumentModel`、诊断 | Golden、空/损坏/闭合轮廓/未知单位/未映射实体 | 检查报告、草稿候选 | 私有 POC 记录；见 [样本规范](../样本与黄金结果规范.md) |
