# LARGE-01：多层 Tileset 与 HLOD

## 目标

在现有 Root + Leaf 基础上设计并验证 Factory Root → Zone/Workshop → Spatial Partition → Detail Content 的多层 Tileset/HLOD。

## 前置、输入与契约

依赖 LARGE-00 的样本与预算、CORE-04 的冻结 Build 和已验证 Scene Package。每层必须定义稳定 ID、Bounds 来源、内容存在性、排序、`refine`、`geometricError`、子节点完整性和失败行为；父级代理内容须有独立来源和验证。

## 范围与非目标

比较固定语义层级、Quadtree 和语义区域加网格，推荐固定语义骨架。不得将现有两层索引重命名为 HLOD，不得以任意常数声称视觉误差保证，不引入 WGS84、隐式分块、Metadata、Cesium 或 IDTS。

## 验证与退出

验证多层 JSON、父子 Bounds、代理与子内容、稳定排序、误差策略、取消和部分失败。只有每个层级的内容与误差可解释、可测并经 Viewer 验收，才能声明 HLOD 已验证。
# CORE-04C 依赖说明

CORE-04C 不实现多层 Tileset 或 HLOD；LARGE-01 的设计、误差策略和真实大厂区验证门槛保持 Planned。
