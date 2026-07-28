# CLI 与作业运行规范

## 当前 v0 基线

当前 CLI **只实现** `scene-builder doctor`，不识别 `inspect`、`build`、`tiles`、`run`，也没有 `--report`、`--job-id`、`--timeout` 或 `--no-cache` 选项。给 v0 传入这些命令/选项会显示 usage 并以退出码 2 结束；不能将下文 v1 预案描述为当前实现。

`doctor` 的参数为可选的 `--output <directory>`、`--blender-path <file>`、`--tiles-path <file>`。它探测 .NET、已显式配置的 Blender 和 Tiles 候选工具；缺失外部工具只报告 `Unavailable`，仍返回 0。指定 `--output` 时，v0 直接在该目录（不是作业目录）写入 UTF-8 `doctor-report.json`；未指定时只输出人类可读摘要。无效参数或把 `--output` 指向现有文件时返回 2；取消时当前实现返回 3。

## 目标 v1 作业协议（尚未实现）

以下规则是 SB-02 之后的目标协议，必须先完成应用层报告适配器、命令解析、测试和迁移门，才能成为可用 CLI 行为。v1 的 `inspect`、`build`、`tiles`、`run` 都要求调用方显式提供 `--output <job-output-root>`；产物固定写在 `workspace/jobs/{jobId}/input|normalized|intermediate|chunks|tiles|dynamic|reports|logs`，其中 `workspace` 是 `--output` 的规范化路径。不得向仓库根目录、`src/` 或 `tests/` 写运行时产物。

v1 `jobId` 必须是调用方指定或由命令生成的安全标识，不能含路径分隔符、`..`、Windows 设备名或控制字符。缓存仅在输入哈希、规则版本、目标报告契约主版本和实际工具版本完全相同时可命中；没有已选定工具、没有产物或已取消的请求不得写入成功缓存。取消时停止启动新外部进程、等待已启动进程至超时，并在报告中记录状态。

| v1 命令 | 必填参数 | 目标行为与门禁 |
| --- | --- | --- |
| `inspect` | `--input`、`--output` | 检查输入并写目标 v1 报告；DXF 解析器 POC 前返回未配置，DWG 返回不支持。 |
| `build` | `--input`、`--rules`、`--output` | 编排检查、规则、SceneDraft 和未来建模；已验证阶段以外不得报告成功。 |
| `tiles` | `--input`、`--output` | 从已验证静态产物请求 Tiles 转换；Tiles POC 前返回未配置。 |
| `run` | `--input`、`--rules`、`--output` | 顺序执行已验证阶段并汇总目标 v1 报告。 |

v1 公共可选参数拟定为 `--job-id`、`--timeout`、`--no-cache` 和 `--report <relative-path>`；其中 `--report` 是**目标 v1 专用**参数，当前不存在。若未指定 `--report`，v1 默认写入 `reports/conversion-report.json`；每次成功创建报告的 v1 命令还写 `logs/command.log`，均使用 UTF-8。

## 目标 v1 退出码与恢复（尚未实现）

| 退出码 | 含义 | 恢复方式 |
| --- | --- | --- |
| 0 | 请求成功，且所请求阶段实际完成 | 保留报告和产物 |
| 2 | 参数、输入路径或作业目录无效 | 修正参数后重试 |
| 3 | 已知未配置或不支持能力 | 完成对应 POC，不生成伪造产物 |
| 4 | 输入/解析/规则诊断错误 | 修正输入或规则后重试 |
| 5 | 超时、取消或外部工具失败 | 检查日志、清理受控进程后重试 |
| 6 | 目标 v1 报告或输出写入失败 | 检查磁盘、权限和输出路径后重试 |

v1 `inspect` 在 DXF 解析器尚未通过 POC 时应写 `DXF_PARSER_NOT_CONFIGURED`、不创建建模产物并返回 3；这不是当前 v0 CLI 可调用的行为。v1 `doctor` 是否迁移到作业协议须独立评审；在迁移前继续遵循本文件的 v0 `doctor-report.json` 例外。
