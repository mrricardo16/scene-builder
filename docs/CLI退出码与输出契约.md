# Scene Builder CLI 退出码与输出契约

## 命令边界

CORE-01 的 CLI 只承载宿主命令：`doctor`、`capabilities`、`help` 和 `--help`。`doctor` 保持既有选项与面向人工的文本输出；`capabilities` 支持 `--format text|json`。在 CORE-02 至 CORE-04 分别交付真实能力前，不得暴露 `analyze`、`plan`、`build` 或 `convert`。

## 退出码

| 退出码 | 含义 | CORE-01 命令映射 |
| --- | --- | --- |
| 0 | 成功 | help、capabilities 和成功完成的 doctor。 |
| 2 | 参数错误 | 未知命令、未知选项、重复或缺失选项值。 |
| 3 | 用户取消 | Ctrl+C 或调用方取消令牌触发的协作式取消。 |
| 4 | 能力未配置或不支持 | 未来需要启动能力但其状态为 NotConfigured 或 Unsupported。 |
| 5 | 操作执行失败 | 未来已注册操作执行失败。 |

Planned 不是运行时操作结果。当前 `capabilities` 仅报告状态，不能把 Planned 映射为一次假成功的转换。

## Capabilities JSON

`capabilities --format json` 写入 UTF-8 无 BOM、严格 JSON。根对象属性顺序固定为 `contractVersion`、`capabilities`；能力对象顺序固定为 `code`、`state`、`diagnosticCode`；能力按注册表稳定顺序输出。输出不得包含绝对路径、机器名、时间、随机 GUID 或客户数据；相同输入必须得到字节一致的输出。

```json
{
  "contractVersion": "1.0",
  "capabilities": [
    {
      "code": "DOCTOR",
      "state": "available",
      "diagnosticCode": null
    }
  ]
}
```

未知命令与非法格式写入 stderr 和稳定帮助，不创建运行时文件，也不抛出未处理异常。
# Analyze 命令

`analyze --input <file> --output <directory> [--rules <file>] [--unit <meters|millimeters|centimeters>] [--format text|json]` 成功返回 0，参数错误返回 2，取消返回 3，Unsupported（含 DWG）返回 4，执行失败返回 5。text/json 仅输出脱敏摘要和相对 Artifact 路径；完整结果固定写入 `analysis/cad-analysis.json`。

# Plan 命令

`plan create --analysis <file> --output <directory>`、`plan validate --plan <file> --output <directory>` 和 `plan freeze --plan <file> --output <directory>` 支持 text/json。创建和冻结成功为 0；无效 Plan validation 仍发布 `validation.json`，但返回 5；参数错误为 2、取消为 3、Unsupported 为 4。

Analysis v2 + Snapshot Available 时，`plan create` 返回 `contractVersion: "2.0"`。`plan validate` 的 JSON 摘要包含 SnapshotId/ContentHash；`plan freeze` 的 JSON 摘要包含 `planContractVersion`、`snapshotId`、`snapshotContentHash`、`buildReadiness` 和 Frozen Plan 相对 Artifact 路径。输出不包含绝对路径、原始规则正文、资产原始路径或 Snapshot 正文。

`buildReadiness: "ready"` 仅表示 CORE-04B 的冻结输入已通过校验；CLI 不因此增加 `scene-builder build`，也不启动任何三维生成器。
