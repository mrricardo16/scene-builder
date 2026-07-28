# IDTS Scene Builder 仓库规则

## 文本编码

- 新建或修改的文本文件必须使用 UTF-8 编码。
- 不得将已有中文注释、中文文案或中文日志改成乱码。
- 如果文件疑似不是 UTF-8，先请求确认；不要直接重写整个文件。

## 仓库边界

- `src/` 仅放置生产项目；`tests/` 仅放置测试项目；`docs/` 仅放置文档与任务卡。
- `SceneBuilder.Domain` 保持独立，不依赖 ACadSharp、Blender、具体 Tiles 转换器或当前 IDTS 清单。
- DXF 是首条验收路径。DWG 与 3D Tiles 在具备经验证实现前只能报告边界或未配置状态，不能声称已支持。

## 执行与输出

- 从仓库根目录执行 `dotnet build SceneBuilder.sln` 和 `dotnet test SceneBuilder.sln`。
- 所有运行时产物必须写入调用方明确提供的作业输出目录；不得写入仓库根目录、`src/` 或 `tests/`。
- `bin/`、`obj/`、测试结果和本地作业产物是可再生文件，不得提交。
