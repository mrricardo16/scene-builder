# CORE-02 CAD 导入分析设计

## 范围

CORE-02 将一个用户指定的 CAD 文件受控复制到显式输出根，并执行结构、几何、轮廓、修复建议和可选规则分类分析，发布稳定的 `CadImportAnalysisResult` 与 `analysis/cad-analysis.json`。它不创建 Conversion Plan、SceneDraft、模型、GLB、Scene Package、3D Tiles、Avalonia UI 或外部进程。

## 已验证的现有能力与边界

1. `ACadSharpDxfInspector` 通过 `IDxfInspector` 读取 DXF，并产生 `CadDocumentModel`。
2. `ACadSharpDxfGeometryExtractor` 通过 `ICadGeometryExtractor` 提取 LINE、LWPOLYLINE、ARC、CIRCLE 和 INSERT；其他实体形成诊断。
3. DXF `$INSUNITS` 映射为 Domain 的 `CadUnit`；未知单位保持 `Unknown`，不会默认猜测。
4. Bounds 使用 `CadBoundsState`（含 `NotEvaluated`、`Empty`、`Computed`），未知不以零坐标代替。
5. Inspector 可提供排序后的 Layer、非布局/非 Xref Block 与 EntityType；名称仅保存在本地受控 Artifact。
6. 现有 Inspector 没有可信 Xref 摘要，因此结果标注 `Unavailable`，不根据名称推断。
7. 现有模型没有代理对象枚举，因此结果标注 `Unavailable`，并保留现有不支持实体诊断。
8. `CadContourBuilder` 从标准化几何构建轮廓，`CadContourValidator` 验证它们；不会自动连接实体。
9. `CadGeometryRepairAnalyzer` 仅生成候选修复计划；CORE-02 不调用修复应用器。
10. 可选规则由严格的 `CadRuleSetJsonLoader` 加载并交给 `CadRuleEngine`；没有规则时分类为 `NotConfigured`，未分类对象保留在摘要中。

## 架构与数据流

Application 定义中立的 `ICadInputAdapter`、请求和响应。其成员只使用仓库自己的 Domain 模型，不泄漏 ACadSharp 类型，不使用 `object`、动态 JSON 或反射扫描。适配器按稳定 `AdapterId` 排序选择，零个或多个匹配都返回稳定 Unsupported/Failed 结果。

Composition 显式构造并注册一个 DXF adapter 与一个 DWG Unsupported adapter；Application handler 不创建 CAD 库对象。未来 Avalonia 使用同一 Host 解析 `CadImportAnalysisHandler`，传入相同 request、progress 与 cancellation token。

```text
input -> controlled input/source.<ext> -> adapter
      -> inspector -> extractor -> normalizer -> contours -> validation -> repair suggestions
      -> optional rule engine -> CadImportAnalysisResult -> analysis/cad-analysis.json
```

DXF adapter 复用 `IDxfInspector`、`ICadGeometryExtractor`、`CadGeometryNormalizer`、`CadContourBuilder`、`CadContourValidator` 和 `CadGeometryRepairAnalyzer`。它不调用 SceneDraftBuilder、Blender、Package、Tiles 或进程启动器。单位仅可用 request 中的显式 override 解决未知值，且来源写入结果。

DWG adapter 复用 `IDwgInspector`/`UnsupportedDwgProbe`，稳定返回 `DWG_UNSUPPORTED`。不读取 DWG、不调用 Core Console/ezdxf、不生成中间 DXF，CLI 映射为退出码 4；能力仍为 `DWG_INPUT = Unsupported`。

## 输入、结果与 Artifact

`CadImportAnalysisRequest` 必须包含 InputPath 与 OutputRootDirectory，可选 RuleSetPath、UnitOverride。输出根由 `IOutputRootPolicy` 验证；输入必须是现有普通文件，不能与目标相同，输出根不能在输入文件目录内。复制使用固定的非敏感名称 `input/source.<ext>`，不覆盖既有文件，也不跟随 reparse point。源始终只读；失败可以保留受控输入副本但绝不发布成功 Artifact。

`CadImportAnalysisResult` 固定 `contractVersion`，以受控输入字节、规则字节和选项 SHA-256 构造 deterministic `analysisId` 与 `sourceFingerprint`，不包含时间、随机 GUID、绝对路径、机器/用户/客户信息。它区分原始与标准化 Bounds 状态，报告结构、支持/不支持实体、几何、轮廓、修复建议、分类、Unclassified、来自明确语义的资产候选和复用的 `SceneDiagnostic`。Xref 与 Proxy 明确是 `Unavailable`。

成功时以 staging 文件原子发布严格 UTF-8（无 BOM）、camelCase、固定属性/集合顺序的 `analysis/cad-analysis.json`，随后严格回读验证。Descriptor 固定为 `Analysis`、`analysis/cad-analysis.json`、validated。失败、取消、Unsupported 不发布 Artifact 并清理 staging。

CLI 的 text 和 JSON 只输出状态、计数、相对 Artifact 与脱敏 diagnostics；不会输出路径、坐标、Layer/Block 全名或客户文本。完整本地 Artifact 仅在调用方给出的输出根，不上传、不写 Git 或公开日志。

## 运行语义

Analyze 使用公共 `SceneWorkflowPhase.Analyze` 与实际执行的稳定 stage code：`ANALYZE_VALIDATE_REQUEST`、`ANALYZE_STAGE_INPUT`、`ANALYZE_INSPECT_DOCUMENT`、`ANALYZE_EXTRACT_GEOMETRY`、`ANALYZE_NORMALIZE_GEOMETRY`、`ANALYZE_BUILD_CONTOURS`、`ANALYZE_VALIDATE_CONTOURS`、`ANALYZE_PLAN_REPAIRS`、`ANALYZE_CLASSIFY`、`ANALYZE_WRITE_RESULT`、`ANALYZE_VALIDATE_RESULT`、`ANALYZE_COMPLETED`。每个外部/同步边界前后检查 cancellation token；既有同步 CAD 读取只能在调用前后观察取消，不能声称可中断库内部读取。取消后不报告完成、不发布 Artifact，CLI 退出 3。参数错误为 2、运行失败为 5、DWG Unsupported 为 4、成功为 0。

## 当前 DXF 支持声明

`ANALYZE` 与 `DXF_ANALYZE` 仅表示受限的现有 DXF 分析链路可通过共享 Host 运行。复杂真实 DXF 的完整支持仍属于 CAD-DXF-01；DWG、Conversion Plan（CORE-03）、Build（CORE-04）、大厂区/HLOD 与 Viewer 均未实现。
