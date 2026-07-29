# CAD 支持矩阵

| 输入或输出能力 | 当前状态 | 验收条件 | 说明 |
| --- | --- | --- | --- |
| DXF 输入检查 | 候选 POC 已实现，`continue-validation`（非正式支持） | 使用私有脱敏 DXF 样本完成实体覆盖、诊断、重复性和 SceneDraft 验收 | ACadSharp 3.6.35 仅通过 `IDxfInspector` 用于最小公开合成样本；尚未被接受为产品依赖，也不得表述为完整 DXF 支持。 |
| DWG 输入检查 | 不支持 | 经独立实现、样本和回归测试验证后才可调整状态 | `UnsupportedDwgProbe` 明确返回 `Unsupported`；不执行转换。 |
| Blender 处理 | 内部最小 GLB 草模已实现，真实工具验证独立记录 | 显式工具路径、受控进程、取消/超时、GLB 校验和公开合成样本 | 仅支持 ClosedProfile Wall/Column、Floor、Area Road；不支持真实设施资产、Baseline/Centerline、分区或 Tiles。 |
| 3D Tiles 转换 | 未配置 | 选定转换器并完成端到端样本验证 | 默认适配器返回 `NotConfigured`，不会声明成功或生成输出。 |

状态“接口已定义”仅表示调用边界已经固定，不表示功能已经实现或可交付。
