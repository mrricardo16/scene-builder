# DESKTOP-01：Avalonia 宿主与 DI

## 目标

在 DESKTOP-00 和 CORE-01 后建立 Avalonia Shell、MVVM 与 DI 组合根。

## 范围与退出

依赖方向为 Views → ViewModels → Desktop Services → Application；Domain 不依赖 Desktop/Avalonia。注册导航、对话框、文件选择、设置、项目存储、作业运行、Doctor 和预览服务。不得在 UI 线程运行分析/Blender，不得改变 Domain 或直接调用底层适配器。启动、未配置状态与依赖边界可测试即退出。
