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

POC 记录必须含样本标识、环境/工具版本、实际命令、结果、失败诊断、许可证结论和决定。缺任一项只能标记 `continue-validation`，不能接受候选。
