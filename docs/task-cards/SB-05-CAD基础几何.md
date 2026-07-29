# SB-05：CAD 基础几何与坐标标准化闸门

## 目标

将模型空间直接 `LINE`、`LWPOLYLINE`、`ARC`、`CIRCLE` 和 `INSERT` 映射为解析器无关的内部基础几何，并在保留原始几何的前提下生成局部米制坐标副本。

## 通过闸门

- 几何契约不引用 ACadSharp，模型空间实体维持来源顺序、来源图层、规范化 DXF 类型和三态范围。
- 单位只按已声明的毫米、厘米、米、英寸或英尺换算；未知和无单位不得猜测。
- 局部原点只在文档范围为 `Computed` 时使用范围最小 X/Y/Z；空文档不伪造原点，未计算范围且存在几何时失败。
- 单个支持实体映射失败时保留其他支持实体，返回部分成功和脱敏诊断；未支持实体仍保留原有 DXF 诊断。

## 当前状态

- 已完成公开合成 DXF 的五类基础实体提取、稳定 `SourceOrder`、LWPOLYLINE elevation/bulge、ARC/INSERT 弧度到度转换、INSERT 位置/缩放和未支持实体的部分成功处理。
- 已完成纯 Domain 的局部米制标准化，保留原始 `CadGeometryDocument`，不原地覆盖来源坐标。
- 不展开 Block，不加载 Xref，不修复轮廓，不运行规则或厂房语义识别，不创建 SceneNode/SceneDraft，不启动 Blender，也不改变 DWG 或 JobReport v0 边界。

## 追溯

| 输入 | 稳定契约 | 测试 | 产物 | 证据 |
| --- | --- | --- | --- | --- |
| 公开合成 DXF | `CadGeometryDocument`、`CadGeometryNormalizer`、`ICadGeometryExtractor` | 值对象、五类实体、单位、范围状态、部分成功、重复执行 | 内部原始/标准化几何文档 | `tests/fixtures/synthetic`、Domain/CAD 测试 |
