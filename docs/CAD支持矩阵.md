# CAD 支持矩阵

状态词含义见 [产品目标与能力状态](产品目标与能力状态.md)。此矩阵描述真实支持边界，而非愿景或测试工具能力。

| 输入/输出 | 状态 | 已有证据 | 尚未通过的闸门 |
| --- | --- | --- | --- |
| 合成 DXF | Partial | 结构摘要、几何、轮廓、修复、分类和 SceneDraft 的公开合成样本链路已实现。 | 匿名真实复杂样本、实体覆盖、重复性、取消和失败诊断。 |
| 真实 DXF | Partial | 现有 DXF Adapter 与私有 POC 提供有限证据。 | 通过 CAD-DXF-01 前不得称为通用生产支持。 |
| DWG 直接解析 | Unsupported | ACadSharp 真实 DWG 直接读取有超时证据。 | 可取消、安全、可重复的真实样本解析及完整摘要。 |
| DWG → DXF 受控转换 | Validated | Core Console 已完成不修改源文件的可行性 POC；转换后真实 DXF 的 ezdxf POC 也有正向证据。 | 转换器许可、安装/版本、取消、超时、Xref、代理对象、真实样本回归和 .NET 产品适配。 |
| 单体 GLB | Validated | 受控 Blender 生成、取消/超时与二进制 GLB 校验已实现。 | 作为统一产品 Build 输出的端到端入口。 |
| Scene Package | Validated | 已实现确定性分区、多 GLB、严格索引和发布验证。 | 作为统一产品 Build 输出的端到端入口。 |
| 本地 Cartesian 3D Tiles 1.1 | Validated | 已从已验证 Scene Package 生成并验证 `tileset.json`。 | Viewer POC、地理配准或 IDTS/Cesium 集成；这些不属于当前能力。 |

## 统一输入适配

未来统一入口以 `ICadInputAdapter` 或实施时确认的等价契约隔离输入：DXF → DXF Adapter → Analyze；DWG → DWG Adapter → 直接解析或受控中间 DXF → DXF Adapter → Analyze。DWG 在未配置时显示“DWG 转换器未配置”，在支持闸门未通过时显示“DWG 支持仍在验证”，均不得启动作业。

原始 CAD 始终只读保存。任何工具版本、许可结论、输入副本指纹、取消、超时、Xref 和代理对象诊断必须由受控作业记录；不能由 UI 或文档虚构成功。
