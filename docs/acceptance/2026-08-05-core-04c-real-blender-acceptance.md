# CORE-04C 真实 Blender 端到端验收记录

日期：2026-08-05

## 环境与范围

- 操作系统：Windows NT 10.0.19044
- .NET SDK：10.0.302
- Blender：4.5.11 LTS，Release，factory-startup 后台模式
- 输入：`tests/fixtures/synthetic/public-synthetic-closed-polyline.dxf`
- 规则：临时验收规则，将 `OUTLINE/LWPOLYLINE` 映射为 3 米墙体；规则未写入仓库
- CAD 2026 环境仅作为已配置环境记录，未启动 AutoCAD，未读取或修改 DWG
- 未涉及 DWG、AutoCAD COM、Avalonia、Viewer、HLOD、LOD 优化、纹理压缩、增量构建或 IDTS

## 命令与结果

1. 在配置的 Blender 根目录递归定位到 `blender.exe`，版本检查 exit `0`。
2. `blender.exe --background --factory-startup --python-expr ...` exit `0`，输出 `SCENE_BUILDER_BLENDER_OK` 和 `4.5.11 LTS`。
3. CLI `help` exit `0`；当前 CLI 不支持子命令 `doctor --help` 与 `build --help`，二者均 exit `2` 并输出总览 Usage，按现状记录。
4. Analyze exit `0`，生成 Analysis v2 与可用 Build Input Snapshot。
5. Plan Create exit `0`；通过项目已有 `SaveRevisionAsync` 发布 revision 2，配置 Single GLB、Scene Package、3D Tiles。
6. Plan Validate exit `0`、`validationStatus=valid`；Plan Freeze exit `0`、`buildReadiness=ready`。
7. Build JSON exit `0`、状态 `succeeded`：
   - `builds/build-0001/scene-draft.json`
   - `builds/build-0001/single-glb/scene.glb`
   - `builds/build-0001/scene-package/scene-package.json`
   - `builds/build-0001/scene-package/partitions/partition-x-p000000-y-p000000.glb`
   - `builds/build-0001/scene-package/tileset.json`
   - `builds/build-0001/build-result.json`
8. 重复 Build exit `0`，JobId 为 `build-0002`，与 `build-0001` 不同；BuildContentId 相同；`build-0001/build-result.json` SHA-256 未变化，两个作业 artifact 目录隔离。
9. 使用现有 `BinaryGlbValidator`、`ScenePackageValidator`、`TilesetValidator` 复核：Single GLB、Scene Package、Tileset 与分区 GLB 均有效；分区 content URI 为安全相对路径。
10. Blender factory-startup 后台重新导入 Single GLB：exit `0`，输出 `SCENE_BUILDER_GLB_IMPORT_OK`，对象数大于 `0`（实际运行观察到 `4`）。
11. CLI text mode exit `0`，输出 `Status: Succeeded`，SingleGlb、ScenePackage、ThreeDTiles 均为 `Succeeded`。
12. 不存在的 Blender 路径：exit `4`，状态 `notConfigured`，仅保留 SceneDraft/report，不生成 GLB、Package 或 Tiles。
13. `--timeout-seconds 0.001`：exit `5`，诊断包含 `BLENDER_PROCESS_TIMED_OUT`，状态不是 cancelled；不生成有效 GLB、Package 或 Tiles，staging 清理完成。

## 能力边界

本次验收只证明当前本地坐标、米制、z-up、root + partition leaves 的受控生成与校验链路。Tileset 使用 local Cartesian 语义；未证明 WGS84、ECEF、HLOD、多层 Tiles、LOD 策略、性能指标或生产级大模型吞吐能力。

## 修复与回归

- 修复 Blender 分区生成在 Windows 深层 staging 路径超过进程启动可用长度的问题：Blender manifest/临时 GLB 改用仓库外短临时目录，最终 artifact 仍移动到作业输出目录并清理 staging。
- 缺失 Blender 可执行文件现在返回 `NotConfigured`，不再继续 Package/Tiles。
- Blender 超时现在保留 `BLENDER_PROCESS_TIMED_OUT`，并跳过下游 Package/Tiles。
- 新增深层输出路径、缺失 Blender、超时行为的回归测试。

## 可复用脚本

```powershell
& .\scripts\acceptance\core-04c-real-blender-smoke-test.ps1 -BlenderPath <actual-blender.exe>
```

脚本默认把作业输出放在仓库外临时目录；可用 `-OutputRoot <directory>` 指定输出目录，失败时可用 `-KeepOutputOnFailure` 保留现场。
