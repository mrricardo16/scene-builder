# IDTS Scene Builder

IDTS Scene Builder 是一个面向 CAD 场景构建的 .NET 8 命令行工具。仓库当前提供可构建、可测试的项目骨架；DXF 是首条计划验收路径。

## 构建与测试

在仓库根目录执行：

```powershell
dotnet build SceneBuilder.sln
dotnet test SceneBuilder.sln
```

## 项目结构

- `src/SceneBuilder.Cli`：命令行宿主。
- `src/SceneBuilder.Domain`：稳定领域契约。
- `src/SceneBuilder.Application`：应用用例与编排入口。
- `src/SceneBuilder.Cad`：DXF/DWG 适配边界。
- `src/SceneBuilder.Blender`：Blender 适配边界。
- `src/SceneBuilder.Tiles`：3D Tiles 适配边界。
- `src/SceneBuilder.Infrastructure`：文件系统与进程等基础设施实现。
- `src/SceneBuilder.Pipeline`：场景构建流水线协调。
- `tests/`：Domain 和 Application 的自动化测试项目。

## 输出目录规则

后续命令必须要求调用者显式提供作业输出目录。所有生成文件（报告、中间文件和最终交付物）都必须位于该目录之下，且不得逃逸到仓库根目录、`src/` 或 `tests/`。默认构建产物 `bin/`、`obj/` 和本地作业目录不纳入版本控制。

## 支持范围

当前骨架不提供 DWG 转换或 3D Tiles 转换。相关项目仅用于建立可替换边界与诊断能力；在完成验证前，不能把它们描述为已支持功能。
