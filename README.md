# IDTS Scene Builder

Scene Builder 是面向 CAD 本地场景构建的 .NET 8 工程。当前仓库已实现受限 DXF 分析链路、SceneDraft、受控 Blender 单体 GLB、Scene Package 和本地 Cartesian 3D Tiles 1.1 的底层构件；它们尚未被统一为可供用户使用的 `convert` CLI 或 Avalonia 应用。

当前 CLI 仅支持：

```powershell
scene-builder doctor [--output <directory>] [--blender-path <file>] [--tiles-path <file>]
```

DXF 是首条验收路径，但现阶段只覆盖明确的合成样本及受限能力。DWG 仍为 Unsupported/ContinueValidation：受控 DWG→DXF 可行性 POC 并不等于正式 DWG 支持。现有 SB-12A 已验证本地 Cartesian 的 Root + 分区 GLB Leaves `tileset.json`，但尚不是大型厂区 HLOD、Viewer 或生产就绪方案。完整边界见 [产品目标与能力状态](docs/产品目标与能力状态.md)、[CAD 支持矩阵](docs/CAD支持矩阵.md) 和 [大厂区 3D Tiles 产品规格](docs/大厂区3DTiles产品规格与验收基线.md)。

从仓库根目录验证：

```powershell
dotnet build SceneBuilder.sln
dotnet test SceneBuilder.sln
```

运行时产物只能写入调用方明确提供的作业输出目录；不得写入仓库根目录、`src/` 或 `tests/`。所有文本文件使用 UTF-8。
