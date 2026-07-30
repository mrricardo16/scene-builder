# LARGE-00：大厂区样本与性能基线

## 目标

用 Small、Medium、Large 与 Stress/Boundary 脱敏样本建立可重复的大厂区 3D Tiles 真实基线，并明确当前 SB-12A 的适用边界。

## 前置、输入与契约

依赖 CORE-04、现有 Scene Package 和 SB-12A；真实 CAD 输入仍受 CAD-DXF-01/CAD-DWG-01 闸门约束。每份证据包含 SampleId、来源类型、版本、文件大小区间、SHA-256 前缀、工具版本、环境、命令、阶段耗时、峰值内存、分区/Tile/GLB/包大小、Global 使用和输出哈希。

## 范围与非目标

测量转换、发布和已配置 Viewer 的加载数据，建立 Target 与 Actual 对照。不得提交真实图纸、路径、Layer/Block 名、坐标、资产或凭据；不得在没有测量前设定 Production Ready，不实现 HLOD、优化、缓存或 Viewer 产品。

## 验证与退出

每类样本重复运行并比较稳定结果和诊断；取消、超时、部分失败和输出隔离均有证据。退出时只能将“已验证大厂区基础负载”应用于满足记录范围的样本，不能上调为 HLOD 或生产就绪。
