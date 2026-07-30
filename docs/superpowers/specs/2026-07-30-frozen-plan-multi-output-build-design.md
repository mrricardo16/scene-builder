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
