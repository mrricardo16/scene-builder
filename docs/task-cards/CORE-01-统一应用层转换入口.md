# CORE-01：统一应用层转换入口

## 目标

提供统一 Analyze / Validate Plan / Build Application 服务与完整 CLI，使 Desktop 和 CLI 调用相同工作流。

## 前置、输入与契约

前置：现有 Domain、CAD、Blender、Pipeline、Tiles 边界保持独立。输入为受控作业请求、输出目录、工具配置和取消令牌。实施时确定服务与 DTO 名称；服务必须暴露阶段进度、诊断和可区分的非成功结果。

## 范围与非目标

实现组合根、CLI 命令、进度/取消映射和输出目录约束。Desktop 不得直连 CAD、Blender 或 Tiles；CLI 不得复制另一套业务流程。非目标是新 CAD 解析器、DWG 支持、Avalonia UI 或修改 Domain 契约。

## 验证与退出

测试 CLI 与 Desktop 替身调用相同入口；取消能传播；进度阶段可显示；所有产物位于作业输出根。退出时才可将“完整 convert CLI”和“统一 SceneConversionService”从 Planned 调整为已实现状态。
