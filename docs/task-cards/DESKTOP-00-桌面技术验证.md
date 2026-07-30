# DESKTOP-00：桌面预览技术验证

## 目标

分别验证 Avalonia/Skia 二维 CAD 预览、离线 WebView + Three.js GLB 预览和 3D Tiles WebView 预览。

## 范围与退出

每项 POC 都验证加载、选择/缩放、资源释放、Windows 发布行为和受控文件白名单；失败时记录结论并回退到受控外部查看器。不得直接交付生产 UI、打包 Blender、把 POC 成功写成当前产品能力，或将三类预览混为一个结论。
