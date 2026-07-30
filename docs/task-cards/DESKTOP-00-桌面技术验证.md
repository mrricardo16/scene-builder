# DESKTOP-00：桌面技术验证

## 目标

在创建实际桌面 MVP 前，验证 Avalonia 上 WebView、随应用提供的离线 Three.js GLB Viewer、受限 .NET/JS 通信、模型/事件资源释放与 Windows 发布可行性。

## 范围与验收

- 这是规划中的 spike，尚未实现；不引入持久产品依赖或转换代码。
- 使用脱敏合成 GLB 验证加载、旋转、缩放、重置、关闭释放和错误显示。
- 验证 Viewer 只能读取显式允许的受控产物，通信不接受任意 URL/脚本。
- 验证 Windows 发布后的运行时要求；关键项失败则记录原因并采用外部 GLB 查看器或已配置 Blender 回退。

## 非目标

不指定 WebView 控件/包，不打包 Blender，不实现 3D Tiles/Cesium，也不把验证成功写成当前产品能力。
