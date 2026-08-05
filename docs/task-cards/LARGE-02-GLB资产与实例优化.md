# LARGE-02：GLB 资产与实例优化

## 目标

为大厂区 Tile 内容建立可测的 GLB 资产、实例和资源预算优化路线。

## 前置、输入与契约

依赖 LARGE-00、LARGE-01 和现有显式 AssetId/确定性 Binding。度量每 Tile 的 Mesh、Material、Texture、Node、Draw Call、GLB 大小、总包大小和重复资产比例；所有优化保持显式资产绑定与 Windows 安全读取边界。

## 范围与非目标

先评估资产复用、GPU 实例化、纹理压缩、Mesh 简化和 Draco/Meshopt 的可行性及许可。不得在无验证前标记任何压缩或 LOD 为已支持，不得重建任意资产匹配、破坏语义/Bounds、修改原始 CAD 或把优化失败降级为静默成功。

## 验证与退出

在 LARGE-00 样本上对比优化前后大小、加载、内存、可视正确性和产物校验；记录每项目标/实际值与回退。退出时只上调已验证的优化项。
# CORE-04C 依赖说明

CORE-04C 只消费 Frozen Plan 中显式资产绑定，不实现 Mesh 优化、实例压缩、Draco、Meshopt 或纹理压缩；LARGE-02 门槛不变。
