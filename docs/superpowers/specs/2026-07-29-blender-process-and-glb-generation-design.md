# SB-10：Blender 进程与最小 GLB 草模设计

## 目标与边界

SB-10 将已经构建完成的内部 `SceneDraft` 映射为版本化 Blender Manifest，并由受控的 Blender 后台进程生成一个最小单场景 GLB。它不重新解析 DXF/DWG、不修改 `SceneDraft`，也不修改 `JobReport v0`。

实现边界如下：

- 支持：`Wall ClosedProfile` 正高度拉伸、`Floor` 平面、`Column` 正高度拉伸、`Road Area` 平面。
- 明确跳过：`Wall Baseline`、`Road Centerline`、静态设施、动态设备、缺少有效墙/柱高度的对象。每个跳过项有稳定语义对象 ID 和诊断。
- 不支持：真实设施资产映射、墙厚、道路 Buffer、门窗、屋顶、材质纹理动画、分区/LOD/3D Tiles、DWG、ezdxf、Xref 与代理对象。

设施在本阶段采用“明确跳过”策略，避免把 Block 名称、源图层或退化 Bounds 变成未验证的资产或尺寸推断。

## 分层与数据流

`SceneBuilder.Domain` 保持独立。`SceneBuilder.Application` 定义生成请求、结果和 `IBlenderSceneGenerator` 边界；`SceneBuilder.Blender` 引用 Application/Domain，包含 Manifest Mapper、进程执行器、GLB 校验器和受信任脚本。

```text
SceneDraft -> Manifest Mapper -> manifest.json
          -> Blender --background --factory-startup --python trusted-script -- ...
          -> staging scene.glb -> GLB Validator -> atomic publish -> BlenderGenerationResult
```

Manifest 是内部契约，`contractVersion` 固定为 `1.0`，单位固定为米。清单只包含 Draft ID、语义对象 ID、可公开的对象类别、轮廓点和必要的高度；不得包含源路径、图层、Block 名、规则 JSON 或 CAD 解析器类型。对象按语义对象 ID 序列化，圆弧按最大 15 度步长离散，Circle 固定 24 点；首尾逻辑闭合但不重复末点。

## 受控进程与文件边界

`BlenderToolOptions` 由调用方显式提供可执行文件、正超时和有限 stdout/stderr 上限。环境变量 `SCENEBUILDER_BLENDER_PATH` 仅可在配置边界转换为该选项；库不扫描注册表或磁盘寻找 Blender。

进程采用 `ProcessStartInfo.ArgumentList`、`UseShellExecute=false`，固定参数为 `--background --factory-startup --python <script> -- --manifest <file> --output <staging>`；不经过 Shell，不拼接命令字符串。工作目录仅在请求的输出根下建立，文件名必须是单一 `.glb` 文件名，不允许绝对路径、`..` 或默认覆盖。生成先写 staging，GLB 校验成功后才移动到最终路径。

进程异步读取有限长度 stdout/stderr，解析一条受控 `SCENEBUILDER_STATUS:` 状态行；超时和取消都终止整个进程树。面对启动异常、非零退出、缺失/非法输出分别返回安全诊断，不回显绝对路径、原始 Manifest 或全部进程输出。

## 可信 Python 与 GLB 校验

Python 脚本随 `SceneBuilder.Blender` 构建复制，UTF-8 编码。它只接收显式 Manifest/Output 参数，严格检查 `contractVersion`、对象 schema、有限坐标和正高度；不使用 `eval`/`exec`、不联网、不从 BlockName 推导资产。脚本用 Blender 内建 mesh API 创建平面或拉伸面，再导出单个二进制 GLB。

发布前，轻量 GLB 校验器检查：文件存在且非空、`glTF` magic、版本 2、声明总长度、首个 JSON chunk、JSON 可解析、`asset.version == "2.0"`、scene 和至少一个 node。校验器只读文件。

## 结果与验证

`BlenderGenerationResult` 的状态为 `Succeeded`、`PartiallySucceeded`、`Failed`、`Cancelled` 或 `TimedOut`。只有通过校验的最终文件才返回 `ArtifactPath`；结果记录生成数、跳过数、稳定跳过 ID 和安全诊断。

自动测试使用 Fake Process Runner，不要求开发机或 CI 安装 Blender。覆盖清单映射、圆弧离散、命令参数、取消/超时/输出上限、GLB 校验和从公开合成 SceneDraft 到有效测试 GLB 的链路。若 `SCENEBUILDER_BLENDER_PATH` 已显式配置，可运行单独的冒烟工具；缺失时不产生 skipped test。
