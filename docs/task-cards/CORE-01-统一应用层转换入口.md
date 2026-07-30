# CORE-01：统一应用层宿主、公共执行契约、组合根与 CLI 框架

## 目标

建立共享 Application Host、跨阶段公共执行契约、能力注册表、组合根和可扩展 CLI 框架。CLI 与未来 Avalonia 都只能调用该共享宿主；当前 `doctor` 必须保持兼容，并新增只读的 `capabilities` 命令。

## 前置、输入与稳定契约

前置：保持 Domain、CAD、Blender、Pipeline 和 Tiles 的既有边界。公共契约只定义 `Analyze`、`ValidatePlan`、`FreezePlan`、`Build` 的操作类型、状态、进度、相对产物描述、`SceneDiagnostic` 和能力状态；不定义 CORE-02 至 CORE-04 的业务请求或结果模型。调用方必须显式提供作业输出根，公共产物只保存其受控相对路径。

## 范围

- 在 Application 建立强类型操作处理器、状态/进度/产物校验和稳定能力注册表。
- 建立不启动外部进程、不扫描 CAD、不创建文件的共享组合根；CLI 与未来 Avalonia 复用该入口。
- 将 `Program.cs` 收缩为编码、取消信号、Host、CLI 应用调用和退出码映射。
- 支持 `doctor`、`capabilities [--format text|json]`、`help` 与 `--help`；为未知命令返回参数错误。
- 冻结退出码：0 成功、2 参数错误、3 用户取消、4 能力未配置或不支持、5 操作失败。

## 非目标

CORE-01 不实现真实 Analyze、计划校验或冻结、真实 Build、完整 `convert`、DWG、Avalonia、Viewer、HLOD 或大厂区优化；不得注册假的 Analyze/Plan/Build 成功处理器。真实能力分别由 CORE-02、CORE-03 与 CORE-04 注册。

## 验证与退出

验证 Host 可由 CLI 和未来 Desktop 复用、Doctor 可解析且文本行为兼容、能力顺序和 JSON 字节稳定、取消映射为 3、非法命令不创建文件。完成后只将共享 Host、CLI 框架、doctor 和 capabilities 标为 Available；完整 `convert` 与统一 Build 仍保持 Planned。当前实现已满足上述 CORE-01 退出条件，真实阶段注册留给后续 CORE 任务。
