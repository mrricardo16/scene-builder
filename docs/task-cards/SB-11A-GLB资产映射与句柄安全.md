# SB-11A：项目级 GLB 资产映射与句柄安全

## 目标

以版本化 Catalog/Binding 将静态设施和动态设备显式映射到 GLB，禁止从 BlockName 推断文件名；资产读取必须抵抗 Windows 重解析点替换。

## 验收

- Catalog/Binding 严格解析、类别隔离、冲突确定性诊断。
- Windows 从可信根目录句柄逐段相对打开，所有段拒绝 Reparse Point。
- Validator 与暂存读取同一最终文件句柄，源路径没有二次打开。
- 资产先写工作区临时文件，校验后发布为匿名 `assets/asset-000001.glb`。
- 非 Windows Fail Closed；不进入空间分区或 3D Tiles。
- Fake 测试与真实 Blender SmokeTest 验证静态、动态导入和最终 GLB。

## 风险与证据

风险：路径检查后按字符串打开会产生 TOCTOU。证据：`SecureAssetFileOpenerTests` 在安全句柄存活时验证路径替换被拒绝，`BinaryGlbValidatorStreamTests` 验证流所有权和位置，SmokeTest 验证真实 Blender 端到端。
