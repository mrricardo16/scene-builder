# DESKTOP-01：桌面宿主与依赖注入

## 目标

在 DESKTOP-00 成功或明确回退后，规划建立 Avalonia Windows 宿主、Shell、MVVM 与 DI 组合根。未来 MVVM 首选 `CommunityToolkit.Mvvm`，实际版本在实施时固定。

## 验收

- 注册窗口、导航、对话框、文件选择、设置、Doctor、作业存储、作业运行器和预览服务。
- Views → ViewModels → Desktop Services → Application 的依赖方向可测试；`SceneBuilder.Domain` 不引用 Desktop/Avalonia。
- 启动不在 UI 线程运行 Doctor、文件扫描或 Blender；页面可显示未配置状态。

## 非目标

不实现转换编排、作业存储或资产配置，也不改变现有 Domain/API 契约。
