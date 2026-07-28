# SB-05：Blender 进程边界闸门

## 目标

定义 Blender 调用协议，但不在本阶段启动外部进程。

## 通过闸门

- `IBlenderProcessRunner.RunAsync` 接收 `BlenderProcessRequest` 和 `CancellationToken`。
- 请求显式携带可执行文件、参数、工作目录和输出目录。
- 结果表达退出码、标准输出、标准错误和状态。

## 当前状态

只有契约，没有进程执行实现；工具路径仍由 doctor 命令探测。

## 追溯

| 输入 | 稳定契约 | 测试 | 产物 | 证据 |
| --- | --- | --- | --- | --- |
| SceneDraft、受控执行请求 | `IBlenderProcessRunner`、进程结果 | 缺失工具、超时、取消、残留进程 | 进程报告、未来 GLB 候选 | [POC 决策记录](../工具选型与POC决策记录.md)；通过前不生成 GLB |
