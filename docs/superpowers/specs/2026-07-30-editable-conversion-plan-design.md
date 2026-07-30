# CORE-03 可编辑 Conversion Plan 设计

## 边界与生命周期

CORE-03 只消费受控 `analysis/cad-analysis.json`，形成 `ConversionPlanDraft` 的不可覆盖 revision、验证结果和不可变 `FrozenConversionPlan`。流程为 Create Draft → Save Revision → Validate → Freeze。它不重新读取 DXF 或运行 Analyze，不调用 `SceneDraftBuilder`、Blender、Scene Package、Tileset 或外部进程，也不生成任何三维产物。

## Analysis 输入

Application 使用严格 UTF-8、camelCase、未知字段拒绝的 DTO reader 读取 Analysis。路径必须是 output root 下的安全相对 `analysis/cad-analysis.json` 或同一受控 root 内的等价绝对路径；拒绝 URL、UNC、`..`、根外路径和损坏/不支持版本 JSON。必须存在非空 `analysisId`、`sourceFingerprint`、合法枚举和 Bounds 状态；不重新解析原始 CAD。读取成功的 Analysis 绑定进入所有 Plan。

## Draft、revision 与内容标识

`PlanId` 是 `analysisId + sourceFingerprint + contractVersion` 的 SHA-256 截断值；`PlanContentId` 是 canonical draft 内容（不含 revision、路径、时间）的 SHA-256。Draft 使用独立记录和只读数组。首次创建为 revision 1、`NotValidated`，默认不应用修复、不加载规则、不绑定资产、不选择输出；已知单位使用 source，未知单位保持未确认。Save 只接受同 PlanId/AnalysisId/Fingerprint/contractVersion 的下一 revision；内容不变返回 `NoChange`，旧 revision 永不覆盖。

文件布局固定为 `plans/revision-000N/plan-draft.json`、`plans/revision-000N/validation.json` 和 `plans/frozen/revision-000N.json`。写入使用 `.staging`、原子 move、无覆盖和严格回读；失败/取消不发布 Artifact。

## 可表达的配置

Input interpretation 仅包含来源单位确认、`UseAnalyzedLocalOrigin`、有限 `ZOffsetMeters` 和规范化的有限 `YawDegrees`。未知来源单位必须明确确认才有效。Repair 仅记录现有 Analysis Repair action id 的启用选择，不应用修复。Classification 是可选严格 `CadRuleSet` 快照；没有对象级覆盖，因为当前 Domain 未提供该契约。Geometry 仅允许 Wall/Column 正有限 `HeightMeters`，不引入厚度、道路宽度、洞口、屋顶、LOD/HLOD。

当前 Asset Catalog/Binding、分区和 Tiles 的实际 Build 契约尚未以可由 Application 安全快照的公开 Plan 合同提供；CORE-03 因此不伪造对应字段。Outputs 仅保存受支持的布尔意图：single GLB、Scene Package、3D Tiles；至少一个为有效，Tiles 要求 Scene Package。Plan 只声明这些将由 CORE-04 消费，不能执行它们。

## Validation 与 Frozen Plan

Validation 检查 Analysis 绑定、单位确认、有限变换、已知 repair action、严格 rule snapshot、正高度和输出依赖。它总是发布与 `PlanContentId` 绑定的 validation Artifact；Invalid 返回失败状态且绝不冻结。Freeze 必须重新读取 draft 和 validation，确保 revision/content id/PlanId 一致且 Valid，然后防御性复制完整 draft 为 Frozen Plan。重复冻结同 revision 返回已有、字节相同 Artifact；不同 revision 独立。Frozen 仅暴露不可变记录/数组，之后修改 draft 不会影响它。

## Application、CLI 与未来 UI

`IConversionPlanService` 位于 Application，由 Composition 显式构造并从 Host 暴露。它使用公共 Plan progress stages，收敛取消为 `Cancelled`，并复用 `SceneDiagnostic`。CLI 增加 `plan create --analysis --output`、`plan validate --plan --output`、`plan freeze --plan --output` 的 text/json 脱敏摘要。Avalonia 将调用同一 service，而不是编辑内部文件路径或自行实现验证。

`PLAN_CREATE`、`PLAN_VALIDATE`、`PLAN_FREEZE` 在 service 接入后为 Available；Build 能力仍 Planned，DWG 仍 Unsupported。

## 确定性与安全

canonical JSON 固定 camelCase、声明顺序和排序集合，无时间、随机 GUID、机器/用户、绝对路径或客户路径。CLI 不输出 Layer/Block 名、坐标或绝对路径。所有中间诊断清除 `SourcePath`。所有入口前后检查 cancellation token。测试覆盖首次 Draft、revision 不覆盖、非法 Analysis/Plan、未知单位、输出依赖、validation/hash freeze 绑定、确定性和 frozen 防御复制，并以依赖边界证明不调用三维流水线。
