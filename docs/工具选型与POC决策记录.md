# 工具选型与 POC 决策记录

任何候选工具在本表完成证据闭环前都不是产品依赖，也不得在 [CAD 支持矩阵](CAD支持矩阵.md) 标为“已支持”。候选名称可在 POC 时补充；当前不预设供应商或许可结论。

| 领域 | 候选类别 | 必须验证 | 通过门 | 未通过时状态 |
| --- | --- | --- | --- | --- |
| DXF | .NET DXF 解析库、受控命令行解析器 | .NET 8 兼容、许可证、合成/匿名样本实体覆盖、错误诊断、重复性 | 固定样本可产出正确图层/实体/单位/范围与 Golden 报告 | `DXF_PARSER_NOT_CONFIGURED` |
| DWG | 商业/开源 SDK、受控离线转换器 | 合法许可证、无网络依赖、版本兼容、Xref/代理对象行为、取消 | 匿名真实样本可重复且失败可诊断 | `DWG_UNSUPPORTED` |
| Blender | 固定版本的 Headless Blender 运行时 | 安装与许可证、脚本输入白名单、超时/取消、残留进程、GLB 检查 | 合成 SceneDraft 可生成并校验 GLB | `BLENDER_NOT_CONFIGURED` |
| 3D Tiles | 离线 Tiles 转换器或内部适配实现 | 许可证、离线执行、tileset 校验、坐标一致性、IDTS 浏览器加载 | 静态产物与动态 GLB 对齐且失败隔离 | `TILES_NOT_CONFIGURED` |

## 决策记录模板

每次 POC 必须在私有或公开（无敏感数据）证据中填写以下 JSON 结构。`decision` 只允许 `accepted`、`rejected`、`continue-validation`。

```json
{
  "recordVersion": "1.0",
  "pocId": "dxf-poc-001",
  "domain": "DXF",
  "sampleIds": ["synthetic-wall-001"],
  "environment": {
    "os": "record-at-run-time",
    "dotnet": "record-at-run-time"
  },
  "candidate": {
    "name": "pending-selection",
    "version": "unknown",
    "licenseConclusion": "pending"
  },
  "command": "recorded-in-private-evidence",
  "result": "not-run",
  "diagnosticCodes": [],
  "decision": "continue-validation",
  "rationale": "尚未执行 POC。"
}
```

## DXF POC 当前候选：ACadSharp 3.6.35

- **候选范围**：仅用于 SB-03 的公开合成 ASCII DXF 检查 POC；实现必须位于 `IDxfInspector` 之后，领域模型不得暴露 ACadSharp 类型。
- **候选版本**：NuGet 包 `ACadSharp` 3.6.35，NuGet 页面声明包适用于 .NET Standard 2.0 及更高目标框架，并提供 ASCII DXF 读写能力。
- **许可证证据**：NuGet 3.6.35 页面标注 MIT license；项目官方仓库 `DomCR/ACadSharp` 的 `LICENSE` 文件为 MIT License，版权为 Albert Domenech（2021）。核验日期：2026-07-28。
- **当前决定**：`continue-validation`。许可证和公开合成样本已完成初步核验，但尚未完成匿名真实样本、实体覆盖、失败诊断一致性、重复性与运行环境证据，不能视为已接受依赖，也不得修改公开 CAD 支持状态。
- **本轮验证证据**：`tests/fixtures/synthetic/` 下的 `public-synthetic-wall.dxf`、`public-synthetic-empty.dxf`、`public-synthetic-closed-polyline.dxf`、`public-synthetic-unitless-line.dxf` 和 `public-synthetic-unmapped-circle.dxf`，以及对应的 8 项 `SceneBuilder.Cad.Tests` 单元测试。已验证 `LINE`、闭合 `LWPOLYLINE` 的范围/图层计数，以及 `DXF_DOCUMENT_EMPTY`、`DXF_UNIT_UNKNOWN`、`DXF_ENTITY_UNSUPPORTED` 三个稳定诊断；`CIRCLE` 只保留通用范围/图层信息，不暴露原始实体负载。
- **未验证门禁**：必须在私有脱敏真实样本上验证图层/图块命名、Xref、代理对象、更多实体、错误诊断一致性与可取消性；还必须在目标 Windows/.NET 运行环境中以固定样本至少重复执行 10 次，记录工具版本、命令、耗时、哈希和结果摘要。以上私有样本与重复性证据未完成前继续维持 `continue-validation`。

POC 记录必须含样本标识、环境/工具版本、实际命令、结果、失败诊断、许可证结论和决定。缺任一项只能标记 `continue-validation`，不能接受候选。
