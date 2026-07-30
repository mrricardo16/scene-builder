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
