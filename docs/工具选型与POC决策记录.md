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

## DWG POC record: ACadSharp 3.6.35 (2026-07-28)

- **Scope:** two private, anonymized real-world DWG samples. The evidence uses only `dwg-private-001` and `dwg-private-002`; no source names, paths, drawing text, coordinates, or drawings are stored in this repository.
- **Executed candidate:** `ACadSharp` 3.6.35 through a private process-isolated direct-read probe, with one 30-second-limited attempt per sample. The synchronous reader is not published as an in-process product adapter because its underlying read cannot be cancelled safely.
- **Result:** both attempts returned `Unavailable` with `DWG_PROBE_TIMED_OUT`; entity, layer, block, unit, and bounds values were unavailable. The private report records byte size and a 12-character SHA-256 prefix only.
- **License conclusion:** the candidate package is MIT according to the previously recorded package and repository license review; runtime compatibility remains unaccepted because the private direct-read POC did not complete.
- **Decision:** `continue-validation`. DWG is not a supported product input, and no public CAD support status changes.
- **Core Console feasibility POC:** the initial `SAVEAS` script was invalid because its prompt order did not match the installed AutoCAD command flow, and its ASCII script encoding could not represent a Chinese output directory. The corrected private POC used `/readonly`, `FILEDIA=0`, `CMDDIA=0`, `DXFOUT`, an ASCII-only private output path, precision 16, and `QUIT`.
- **Core Console result:** AutoCAD Core Console 25.1.60.0.0 exited with code 0 and produced a nonempty ASCII DXF whose header identifies `AC1032` and whose tail contains `EOF`; the private source fingerprint was unchanged. This establishes only the feasibility of the controlled DWG-to-DXF conversion step, not product DWG support.
- **Downstream result:** ACadSharp 3.6.35 did not complete inspection of this private converted DXF within a 10-minute isolated-process limit. The DXF parser is therefore not accepted for the converted real-world output.
- **Repeat-conversion evidence:** the corrected Core Console flow also completed on the separately anonymized sample and produced a nonempty `AC1032` DXF. Both source drawings emitted missing-SHX warnings; this does not invalidate the conversion evidence, but text and shape-symbol fidelity remain unverified.
- **Required next evidence:** establish converter licensing, Xref/proxy-object fidelity, cancellation and reproducibility; and select or improve the DXF parser before any product integration.

## DXF POC 当前候选：ACadSharp 3.6.35

- **候选范围**：仅用于 SB-03 的公开合成 ASCII DXF 检查 POC；实现必须位于 `IDxfInspector` 之后，领域模型不得暴露 ACadSharp 类型。
- **候选版本**：NuGet 包 `ACadSharp` 3.6.35，NuGet 页面声明包适用于 .NET Standard 2.0 及更高目标框架，并提供 ASCII DXF 读写能力。
- **许可证证据**：NuGet 3.6.35 页面标注 MIT license；项目官方仓库 `DomCR/ACadSharp` 的 `LICENSE` 文件为 MIT License，版权为 Albert Domenech（2021）。核验日期：2026-07-28。
- **当前决定**：`continue-validation`。许可证和公开合成样本已完成初步核验，但尚未完成匿名真实样本、实体覆盖、失败诊断一致性、重复性与运行环境证据，不能视为已接受依赖，也不得修改公开 CAD 支持状态。
- **本轮验证证据**：`tests/fixtures/synthetic/` 下的 `public-synthetic-wall.dxf`、`public-synthetic-empty.dxf`、`public-synthetic-closed-polyline.dxf`、`public-synthetic-unitless-line.dxf` 和 `public-synthetic-unmapped-circle.dxf`，以及对应的 8 项 `SceneBuilder.Cad.Tests` 单元测试。已验证 `LINE`、闭合 `LWPOLYLINE` 的范围/图层计数，以及 `DXF_DOCUMENT_EMPTY`、`DXF_UNIT_UNKNOWN`、`DXF_ENTITY_UNSUPPORTED` 三个稳定诊断；`CIRCLE` 只保留通用范围/图层信息，不暴露原始实体负载。
- **未验证门禁**：必须在私有脱敏真实样本上验证图层/图块命名、Xref、代理对象、更多实体、错误诊断一致性与可取消性；还必须在目标 Windows/.NET 运行环境中以固定样本至少重复执行 10 次，记录工具版本、命令、耗时、哈希和结果摘要。以上私有样本与重复性证据未完成前继续维持 `continue-validation`。

## DXF POC 新候选：ezdxf 1.4.4

- **候选范围**：仅用于独立私有 POC；在受控的隔离 Python 3.14.4 虚拟环境中安装，未接入 .NET 产品代码、`IDxfInspector` 或公开 CLI。
- **许可证与兼容性证据**：ezdxf 1.4.4 的官方文档与 PyPI 元数据均标示 MIT 许可证，支持 Python 3.14 和 `AC1032`（AutoCAD 2018）DXF。
- **本轮结果**：对两份私有、由 Core Console 生成的 `AC1032` ASCII DXF，读取、图层/Block/模型空间实体枚举均成功；私有报告记录版本、单位、图层数、Block 数、实体类型计数和耗时。一个样本的单位为 0，映射为 `CadUnit.Unitless` 并产生 `DXF_UNIT_UNKNOWN`；另一个样本的 `INSUNITS` 为 4，映射为 `CadUnit.Millimeters`。不得擅自推断未声明单位。
- **重复性证据**：在固定的第二份私有样本上连续读取 10 次，10 次均成功，且 DXF 版本、单位、图层/Block/实体及实体类型摘要一致。耗时范围仅记录在私有证据中；它证明该环境下的结果摘要可重复，不构成生产性能或容量承诺。
- **映射 POC**：私有映射报告仅保存脱敏样本标识、领域枚举、图层/实体摘要和稳定摘要；不保存绝对路径、图层名或 Block 名。它验证了单位、图层摘要和稳定键可在不泄漏实现类型的条件下映射，但当前 `CadDocumentModel` 没有 Block 目录，且不能区分“尚未计算范围”与空范围，因此结果只能标记为 `Partial`，不能伪造完整映射。
- **Xref 与代理对象观察**：两个样本均未检测到 Xref 定义或模型空间 Xref 引用，因此当前证据不能验证 Xref 保真，必须用受控 Xref 样本另行验证。一个样本含 `ACAD_PROXY_ENTITY` 及其他 AutoCAD 扩展数据；ezdxf 的只读代理图形提取未出现失败，但该图形不等同于原始对象语义，不能用作语义保真证据。
- **已知限制**：通用递归包围盒计算会遍历复杂块并产生扩展数据警告，尚未形成可接受的范围结果；范围计算留在几何标准化 POC，不把它误报为解析器成功能力。
- **当前决定**：`continue-validation`。ezdxf 是真实 DXF 的可行解析候选，但尚未完成受控 Xref 样本、代理对象语义保真、取消/资源限制，以及 Block 目录和范围语义的契约设计，不能作为已接受依赖或正式 DXF 支持。

POC 记录必须含样本标识、环境/工具版本、实际命令、结果、失败诊断、许可证结论和决定。缺任一项只能标记 `continue-validation`，不能接受候选。
