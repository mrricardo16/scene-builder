# Scene Builder 交互式转换工作流设计

## 目标和边界

本设计定义未来 Application 层和 Avalonia 共同使用的交互式转换工作流。它不实现代码、不改变 `SceneBuilder.Domain`、不修改现有 CAD、Blender、Scene Package 或 Tiles 契约，也不宣布 DWG 已经支持。

工作流分为三个不可混淆的阶段：Analyze、Edit Plan 和 Build。Desktop 只能调用 Application 统一入口；不得由 ViewModel 直接调用 ACadSharp、`CadRuleEngine`、`SceneDraftBuilder`、Blender 或 Tiles 生成器。

## 阶段 A：Analyze

Analyze 接收受控作业目录中的 CAD 输入，只解析、标准化、分析和诊断，绝不启动 Blender。实施时应基于现有架构确定精确名称；建议契约为 `ICadImportAnalysisService`、`CadImportAnalysisRequest` 和 `CadImportAnalysisResult`。

分析结果至少包含输入类型与版本、单位状态、原始与局部 Bounds、Layer/Block/实体类型摘要、未支持实体、Xref、代理对象、轮廓、修复建议、分类摘要、未分类对象、资产候选和诊断。单位不明时不得静默猜测；结果必须保留来源和诊断。

输入适配边界建议为 `ICadInputAdapter`：DXF 经 DXF Adapter 进入 Analyze；DWG 只能在适配器状态已通过支持闸门时直接解析或先受控转换为中间 DXF。转换器未配置时显示“DWG 转换器未配置”，验证未完成时显示“DWG 支持仍在验证”，两种情况均不启动作业。

## 阶段 B：Edit Plan

Analyze 产生 `ConversionPlanDraft`。它是 UI 可编辑模型，不是原始 CAD，也不是最终 `SceneDraft`。至少包含 `PlanId`、`Revision`、`Dirty`、`ValidationStatus`、`SourceAnalysisId`、`CreatedAt` 和 `UpdatedAt`。

用户只能编辑有明确现有语义和验证规则的参数。输入解释可包含显式单位确认、局部原点策略、Z Offset、平面旋转、是否启用受控修复及其容差；用户覆盖必须记录来源和审计信息，只影响后续计划，不修改原始 CAD。规则编辑必须生成或修改现有严格规则契约，不能在 ViewModel 重写分类排序、允许任意脚本或绕过 `CadRuleEngine`。首版几何编辑限于当前已支持的 `Wall HeightMeters` 与 `Column HeightMeters`；厚度、道路宽度、洞口、屋顶和网格简化均为 Planned，不能提前伪造字段。

资产映射复用显式 `AssetId`、确定性 Binding、Windows 安全文件读取和匿名暂存；禁止按 BlockName 猜文件名或扫描目录匹配任意 GLB。输出计划支持单体 GLB、Scene Package、3D Tiles，以及已有分区/tiles 参数和输出目录的范围校验。

通过验证后，草稿生成不可变 `FrozenConversionPlan`。Build 只消费冻结快照；Blender 在用户调参期间不得读取可变 ViewModel。

## 阶段 C：Build

Build 消费 `FrozenConversionPlan`，按输出选项生成单体 GLB、Scene Package 和/或 3D Tiles 1.1。它不得重新猜测 UI 参数，也不得修改计划版本。Application 层负责阶段化进度、协作式取消和诊断；Desktop 只显示受控快照。

Build 作业完成后必须验证产物，再允许预览。失败、取消、未配置工具或未通过支持闸门都必须保持非成功状态，不得伪造产物可用。

## 进度、取消与数据隔离

统一入口应定义可枚举的阶段进度与可空百分比；未知百分比显示“正在处理”，不能伪造 100%。取消使用 `CancellationToken` 协作传播，Application 层负责阻止尚未启动的外部进程并在现有受控边界内收敛已启动进程。

原始输入只读保存。项目、分析、计划修订、Build Job 和 Artifact 是五个独立对象；每次重新生成使用新的 Build Job，永不覆盖旧作业产物。所有运行时路径必须位于调用方明确的输出根或未来 LocalAppData 项目根内。
