# IDTS 集成适配预案

IDTS 集成是后置适配工作，不改变 `SceneBuilder.Domain` 的稳定模型。唯一允许的运行调用方向是：IDTS Worker 提交明确的 CLI 作业请求，读取 `conversion-report.json` 与已验证产物，再将其转换为当前 IDTS 的导入请求。Web Controller 不得直接启动 Blender、CAD 转换器或 Tiles 工具。

## 输入、输出与失败处理

| 边界 | 输入 | 输出 | 失败语义 |
| --- | --- | --- | --- |
| Worker → CLI | 私有输入位置、规则版本、作业输出目录、超时 | CLI 退出码、报告相对路径 | 非 0 不导入任何不完整产物 |
| CLI → Worker | `conversion-report.json`、`artifacts` 相对路径 | 验证后的静态/动态资产候选 | 状态非 `succeeded` 或缺报告即拒绝导入 |
| Worker → IDTS | 适配后的资产元数据 | IDTS 导入结果 | IDTS 失败只标记导入失败，不篡改 CLI 报告 |

适配器负责坐标、比例、轴向和资产绑定的 IDTS 特有转换；这些字段不得反向加入 `SceneDraft`。首次集成前必须以合成场景验证静态产物和动态 GLB 的共同局部原点，并在 POC 记录中保留浏览器加载证据。不得在公开文档或日志中记录 IDTS URL、令牌、项目 ID 或资产 ID。
