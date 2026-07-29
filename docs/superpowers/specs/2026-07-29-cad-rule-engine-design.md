# CAD 规则引擎设计（SB-08）

## 目标与边界

SB-08 在可信的二维 CAD 中间结果上执行项目级、纯内存的语义分类：`NormalizedCadGeometryDocument`、修复后的 `CadContourDocument` 与已验证的 `CadRuleSet` 形成 `CadClassificationResult`。它位于几何提取、轮廓验证和 SB-07 受控修复之后；不修改其输入，不生成 `SceneDraft`、三维几何、JobReport 字段或 DWG 能力，也不启动外部进程。

Domain 只承载规则契约、验证、Subject 构建和匹配引擎，不引用 ACadSharp、JSON 或文件系统。Application 以 DTO 和 `System.Text.Json` 加载规则，使用显式映射后才交给 Domain；未知 JSON 字段一律拒绝，不能执行脚本、正则、反射或动态代码。

## Subject

分类对象统一为 Contour、OpenSegment 和 Insert。有效轮廓、开放段与标准化 `INSERT` 都保留为 Subject；无效轮廓保留但不可匹配，结果为 `unclassified` 并带匿名 `RULE_SUBJECT_INVALID` 证据。Contour 使用既有轮廓 Id，OpenSegment 使用既有段 Id，Insert 使用 `insert:{sourceOrder}`；Id 不含图层、Block、路径或坐标。轮廓只有来源 Layer 和 EntityType 一致时可匹配；Circle 使用自身来源字段，SegmentContour 使用其段来源字段。Block 仅来自实际 `CadInsertGeometry.BlockName`，绝不使用 Block 定义摘要或展开 Xref。

## 规则契约与验证

`CadRuleSet` 固定支持 `contractVersion = "1.0"`，规则集合非空，规则 Id 以 `Ordinal` 唯一。固定分类为 `wall`、`column`、`floor`、`road`、`static-facility`、`dynamic-equipment` 和默认 `unclassified`；`unclassified` 只能是引擎默认结果，不能作为启用规则的目标，未知枚举或 JSON 文本拒绝。规则至少含 Layer、Block、EntityTypes 之一；Layer/Block 不得空白，EntityTypes 无空项、按 Ordinal 去重并在 JSON 映射时规范为大写。GeometryDefaults 只支持 `heightMeters`，必须有限且非负。

无效 RuleSet 整体失败并只输出 `RULE_CONFIG_INVALID`，不以部分规则继续分类。配置和结果均为内部契约，不能直接作为公开报告 JSON；诊断不写入完整 JSON、真实 Layer/Block、路径或坐标。

## 受控匹配与排名

Layer/Block 使用完整字符串、OrdinalIgnoreCase 的自有 `*`/`?` 通配算法；不使用正则和文化相关比较。无通配符即精确匹配。EntityType 是先决过滤，不为 Layer/Block 加分；Block 条件只有 Insert 且 BlockName 存在时可能匹配。所有已声明条件必须同时命中，不能降级。

冻结 rank 为：Block 精确+Layer 精确 600、Block 精确+Layer 通配 500、仅 Block 精确 490、Block 通配+Layer 精确 400、仅 Layer 精确 390、Block 通配+Layer 通配 300、仅 Block 通配 290、仅 Layer 通配 200、仅 EntityType 100、未分类 0。先 rank 降序，再 priority 降序；输入规则顺序不参与结果。

相同 rank 与 priority 的不同分类为 `RULE_CONFLICT`：对象保持未分类，结果 `PartiallySucceeded`。相同分类为 `RULE_DUPLICATE_MATCH`：按 Rule Id Ordinal 选择最小者。未匹配不是失败。Subject、候选 Rule Id、对象结果和诊断均按稳定 Ordinal 顺序输出。

## 状态、性能与验证

有效 RuleSet 且无冲突为 `Succeeded`；有效 RuleSet 且至少一个冲突为 `PartiallySucceeded`；输入或规则无效为 `Failed`，不伪造对象结果。复杂度为 `O(S × R)`，规则先验证一次，通配匹配不包含无界回溯或 JSON 重复解析。测试覆盖固定 rank、禁用规则、冲突/重复、规则和 Subject 乱序、无效配置、Contour/OpenSegment/Insert、JSON 严格读取及 DXF→几何→轮廓→分类的公开合成链路。
