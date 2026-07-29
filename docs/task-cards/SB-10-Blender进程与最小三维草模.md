# SB-10：Blender 进程与最小三维草模

## 目标

将内部 `SceneDraft` 安全转换为单场景 GLB 草模，并在发布产物前校验 GLB 基础结构。

## 输入、输出与证据

- 输入：SB-09 的 `SceneDraft` 与调用方显式提供的 Blender 工具选项、输出目录和输出文件名。
- 输出：版本化内部 Manifest、经校验的 GLB（仅成功/部分成功时）、`BlenderGenerationResult`。
- 证据：Mapper、进程边界、校验器与端到端 Fake Process 测试；可选真实 Blender 冒烟记录。

## 进入条件

- SB-09 的语义对象与 `SceneDraft` 契约已存在。
- 运行产物有调用方明确指定的输出根目录。

## 退出条件

- Wall ClosedProfile、Floor、Column、Road Area 可写入 Manifest 并由可信 Blender 脚本生成最小网格。
- Baseline、Centerline、设施和缺高对象被稳定诊断并跳过。
- 进程不经 Shell，支持取消、超时、有限输出及进程树终止。
- GLB 在发布前通过 magic、版本、长度、JSON、scene/node 校验。
- 真实 Blender 验证仅在显式工具路径存在时执行；不提交生成的二进制产物。

## 非目标

不交付资产映射、墙厚、道路 Buffer、材质/纹理/动画、分区、LOD、3D Tiles、DWG/ezdxf/Xref、代理对象语义或 `JobReport v0` 变更。
