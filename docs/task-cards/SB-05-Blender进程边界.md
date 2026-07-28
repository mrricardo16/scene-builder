# SB-05：Blender 进程边界闸门

## 目标

定义 Blender 调用协议，但不在本阶段启动外部进程。

## 通过闸门

- `IBlenderProcessRunner.RunAsync` 接收 `BlenderProcessRequest` 和 `CancellationToken`。
- 请求显式携带可执行文件、参数、工作目录和输出目录。
- 结果表达退出码、标准输出、标准错误和状态。

## 当前状态

只有契约，没有进程执行实现；工具路径仍由 doctor 命令探测。
