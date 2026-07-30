# Scene Builder 共享 Application Host 与 CLI 设计

## 冻结边界

CORE-01 建立共享宿主、跨阶段公共执行契约、能力注册、组合根与可扩展 CLI 框架。它不实现真实 CAD Analyze、Conversion Plan、冻结计划、产品级 Build、完整 convert、Avalonia 或 DWG。后续 CORE-02、CORE-03、CORE-04 分别注册真实 Analyze、Plan 与 Build 处理器，且不能以 Planned 能力伪造成功结果。

## 选择

采用独立的 `SceneBuilder.Composition` 项目，而不是让 CLI 或 Infrastructure 充当组合根。该项目当前只引用 Application 和 Infrastructure，手工创建不可变 Host，因此不增加 `Microsoft.Extensions.*` 包、不产生 Service Locator、也不缓存作业路径。CLI 仅引用 Composition；未来 Avalonia 通过同一工厂创建 Host。

## 公共契约

Application 定义操作类型、结果状态、阶段化进度、相对产物描述、强类型 `ISceneOperationHandler<TRequest, TResult>`、能力状态与能力表。`SceneOperationResult` 继续承载既有 Domain `SceneDiagnostic`，集合永不为空。进度和产物路径由无副作用校验器验证：未知百分比保持 null，百分比必须有限且在 0 至 100；路径必须是使用 `/` 的受控相对路径，不允许绝对路径、UNC、`..`、反斜杠或 URI scheme。

## Host 与 CLI

Composition 工厂注册 Doctor probes 和稳定能力表，创建 Host 不启动 Blender、不扫描 CAD、不创建文件。CLI Application 接收 Host、输入/输出抽象和取消令牌；Program 只设置 UTF-8、订阅 Ctrl+C、创建 Host、调用应用并返回退出码。`doctor` 使用原有 Doctor parser 和 report writer；`capabilities` 以确定性 text 或严格 JSON 表达能力状态。输出与退出码遵循 [CLI 退出码与输出契约](../../CLI退出码与输出契约.md)。

## 验证

测试从公共进度、产物、能力表和 CLI parser 的 RED 开始，覆盖非法值、路径逃逸、稳定排序、Doctor 兼容、未知命令、JSON 稳定性、取消及无文件副作用。组合根测试确认可解析 Doctor 和 registry，且创建 Host 不启动外部进程或创建文件。
