# CORE-04 冻结计划与多格式生成设计

## 已核实的 Build 输入边界

当前 `CadImportAnalysisResult` Artifact 只保存结构/几何统计摘要，`ConversionPlanDraft`/`FrozenConversionPlan` 只保存计划意图。`SceneDraftBuilder` 实际需要 `CadDocumentModel`、`NormalizedCadGeometryDocument`、`CadContourDocument` 和 `CadClassificationResult`；这些数据不在现有 Artifact 中。因此当前 v1 Analysis 和 v1 Frozen Plan 不能 Build，且 Build 绝不能通过重开 DXF 补足数据。

选择扩展 CORE-02 的 Analysis Artifact，而不是在 Frozen Plan 复制一份 Snapshot：同一个已验证 Analysis 是多个 revision 的共同、可审计输入。新增版本化 `buildInputSnapshot`，仅包含仓库中立模型，不含 ACadSharp、原始 DXF、绝对路径、时间或随机值。CORE-04 Build 只接受含 Snapshot 的新 Analysis 和明确引用它的新版 Frozen Plan；旧 Artifact 返回 `FROZEN_PLAN_NOT_BUILD_READY`/`PLAN_REFREEZE_REQUIRED`，不静默迁移。

## Frozen Build 配置

Freeze 将 Draft 的输出意图解析成版本化 `FrozenBuildConfiguration`。语义配置（单位确认、局部坐标、Z Offset、Yaw、修复选择、规则/分类 Snapshot、高度、资产绑定、输出、分区、Tiles）必须在 Frozen Plan；运行环境（Blender executable、timeout、临时路径、并发）仅在 Build request，且不能覆盖语义。当前无安全持久 Asset catalog/binding snapshot 时，纯程序化场景可 Build；如 Snapshot 中存在 StaticFacility/DynamicEquipment，Build Readiness 失败，绝不按 BlockName 猜资产。

## 输出依赖与复用

```text
Build Snapshot -> SceneDraftBuilder
               -> Single GLB -> GLB validator
               -> Scene Package -> package validator -> 3D Tiles -> tileset validator
```

3D Tiles 必须使用同一个已验证 Scene Package；Package 失败会阻止 Tiles，单体 GLB 失败不阻止 Package。所有生成器复用既有 `IBlenderSceneGenerator`、`ScenePackageGenerator`、`TilesetGenerator` 和验证器，Application 只做薄契约映射和调度，不复制算法。

## Job、Artifact 与恢复

每次 Build 分配安全且不覆盖的 `builds/build-000N`。BuildContentId 由 FrozenPlanId、Snapshot 内容、输出选择和工具版本标识确定；JobId 不参与语义。生成器写 staging，Application 只发布已经验证的相对 Artifact 与 `build-result.json`，其中不含绝对路径、机器、用户或工具路径。部分成功保留已验证 Artifact，依赖被阻止的输出标记 `SkippedDependencyFailed`；取消不启动后续阶段并清理 staging。

## CLI、Capabilities 与非目标

`build --plan <frozen-plan-json> --output <root> [--blender-path] [--timeout-seconds] [--format text|json]` 只提供运行环境，不能覆盖 Frozen Plan。`BUILD`、`BUILD_GLB`、`BUILD_SCENE_PACKAGE`、`BUILD_3D_TILES` 表示产品调度已实现，不表示 Blender 已配置或大厂区性能已验收。Avalonia 调用同一 Build handler。非目标是 Avalonia、DWG、DXF parser/repair/rules重写、HLOD/LOD、多层 Tiles、优化、缓存、Viewer 和大厂区验收。
# CORE-04C 实现补充

当前工作区已实现本地 Build 编排，但能力注册仍保持 `BUILD`、`BUILD_GLB`、`BUILD_SCENE_PACKAGE`、`BUILD_3D_TILES` 为 Planned：本机 `doctor` 尚未发现 Blender，未满足真实 SmokeTest 后再升级能力的门槛。代码路径可以在测试替身下验证，不能据此宣称生产可用。

### 映射与变换顺序

Build 只读取冻结计划和 Snapshot，不调用 CAD Adapter、DXF 解析或 Analyze。Snapshot 已是米制、分析局部原点坐标；冻结输入解释按以下固定顺序应用到所有几何、轮廓、Repair 点、INSERT 位置和资产候选位置：

```text
分析局部米制坐标
  → ExplicitOffset 时减去 Frozen LocalOrigin，否则保持分析原点
  → 绕 Z 轴按 Frozen YawDegrees 旋转
  → 加上 Frozen ZOffsetMeters
  → SceneDraft / GLB / Scene Package / Tiles
```

INSERT 的旋转同时加上 Yaw 并归一化到 `[0, 360)`；Repair 仍通过现有 `CadGeometryRepairApplier`，其 `CanApply`、当前几何匹配和轮廓校验决定应用或跳过，Build 不静默替换失败动作。

### 作业、状态与验证

每次执行通过 claim 文件分配新的 `build-000N`，先在 `.staging-*` 目录写入并验证结果，再一次性移动为 `builds/build-000N`。BuildContentId 只由冻结计划、Snapshot、规则/资产哈希、生成器契约版本和 Blender 文件版本组成，不包含绝对路径、时间、机器名、随机数或 JobId。单体 GLB、Scene Package、Tiles 分别保留状态；Tiles 只消费同一次执行生成的 Scene Package，Package 失败时 Tiles 为 `SkippedDependencyFailed`。取消或失败不会发布未验证的 GLB/Tiles。

CLI 新增：

```text
scene-builder build --plan <frozen-plan-json> --output <root> [--blender-path <file>] [--timeout-seconds <seconds>] [--format text|json]
```

真实 SmokeTest 前的验收证据为：全量 build/test、Frozen Plan Readiness 复检、生成器调用替身测试、job 隔离/路径安全/取消与 NotConfigured 测试，以及 `scene-builder doctor` 的当前环境报告。
