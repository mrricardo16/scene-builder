# DESKTOP-04：作业运行与进度

## 目标

通过规划中的 `DesktopJobRunner` 单并发调度统一 Application 入口，显示 `JobStatus`、`JobStage`、`JobProgress`，支持取消与关闭确认。

## 验收

- 仅经 SB-10.5 的 `SceneConversionService`/CLI 入口调用；桌面端不直接调用 CAD/Blender。
- 同时最多一个 Blender 相关作业；进度更新经 UI Dispatcher，未知百分比不伪造完成度。
- 取消协作式传递，关闭窗口先确认并请求收尾；失败/取消不得显示成功或可用产物。

## 非目标

不改 `JobReport v0`，不强杀外部进程，不新增 CAD/Blender 功能。
