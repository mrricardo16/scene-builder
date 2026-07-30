# 工具选型与 POC 决策记录

候选工具在完成证据闭环前不是产品依赖，也不能改变 [CAD 支持矩阵](CAD支持矩阵.md) 的状态。

| 领域 | 当前证据 | 决策 | 后续闸门 |
| --- | --- | --- | --- |
| ACadSharp DXF | 已实现公开合成样本链路；真实转换后 DXF 未在该解析器中完成。 | continue-validation | 真实匿名样本、实体覆盖、取消、重复性和诊断一致性。 |
| ezdxf 1.4.4 | 私有 POC 可读取受控 DWG→DXF 的真实 `AC1032` DXF，并有摘要重复性证据。 | continue-validation | 不接入 .NET 产品前不得作为产品解析器或 DXF 正式支持。 |
| ACadSharp DWG | 两份匿名真实 DWG 的受控直接读取超时。 | continue-validation | 不得作为 in-process 产品 DWG Adapter。 |
| AutoCAD Core Console | 受控 `DWG → DXF` 可行性 POC 成功，源文件指纹未变化。 | continue-validation | 许可、安装/版本、取消、超时、Xref、代理对象与回归证据。 |
| Blender | 受控 headless GLB 生成与校验代码存在。 | validated for bounded GLB generation | 不代表 Desktop、任意 CAD 或任意资产支持。 |
| 3D Tiles | 内部本地 `tileset.json` 生成器已验证，不依赖外部转换器。 | validated for local Cartesian tileset generation | Viewer、Cesium/IDTS、地理配准仍需独立 POC。 |

每项 POC 至少记录候选工具与版本、许可结论、脱敏样本标识、命令或受控作业、结果、诊断、取消/超时行为、重复性和明确 `accepted`、`rejected` 或 `continue-validation` 决策。私有证据不得提交图纸、坐标、路径、图层名、Block 名、凭据或客户标识。

## DWG 当前结论

DWG 是产品目标输入，但目前不是可用产品输入。ACadSharp 直接读取真实 DWG 超时；Core Console 的 DWG→DXF 仅证明受控转换可行；ezdxf 的转换后 DXF POC 仅为正向验证证据。CAD-DWG-01 完成前，任何入口都必须保持 Unsupported 或 ContinueValidation，绝不伪造导入成功。
