# SB-06 CAD 二维曲线段、轮廓构建与有效性检测设计

## 目的与边界

SB-06 在 SB-05 的 `NormalizedCadGeometryDocument` 之后增加纯内存的二维几何处理链：

```text
NormalizedCadGeometryDocument
  -> CadCurveSegment2
  -> source-defined closed contour construction
  -> validation
  -> CadContourDocument
```

所有算法只使用已标准化的、以米为单位的局部 X/Y 坐标。源 Z 值会随点保留，轮廓不会投影、修复或改写坐标。若一个候选轮廓的 Z 偏差超过平面容差，结果保留并标记 `CONTOUR_NON_PLANAR`。

稳定且与解析器无关的契约、构建器和验证器放在 `SceneBuilder.Domain`。本阶段不新建 `SceneBuilder.Geometry`：算法只服务当前 Domain 中间契约，新增项目会增加依赖面而没有隔离收益。它们不引用 ACadSharp、文件系统、进程或网络；Cad 适配器也不参与轮廓算法。

本阶段不做散乱 LINE 自动拼接、吸附、顶点移动、重复消除、自交修复、布尔运算、洞与外环关系、规则分类、SceneDraft/SceneNode 生成、Block 展开、DWG、Blender、GLB、Tiles、外部进程、HTTP、数据库或 IDTS 集成。

## 段契约与源映射

`CadCurveSegment2` 记录稳定的源序号、段序号、图层和 DXF 实体类型，并由 `CadLineSegment2` 与 `CadArcSegment2` 实现。点、半径和角度必须是有限值；线段允许零长度，以便验证器给出可追溯诊断。圆不伪装成首尾相同的圆弧，而使用 `CadCircleContour` 表示自然闭合轮廓。

输入只接受 `NormalizedCadGeometryDocument`：

| 源实体 | 输出 |
| --- | --- |
| `LINE` | 一个开放直线段 |
| 开放 `LWPOLYLINE` | 相邻顶点的开放段；每段使用起点 bulge |
| 闭合 `LWPOLYLINE` | 相邻段外加最后顶点至第一顶点的源定义闭合段 |
| `ARC` | 一个开放圆弧段，SB-05 已提供角度制，不再次转换 |
| `CIRCLE` | 一个自然闭合圆轮廓 |
| `INSERT` | 保留在标准化文档中，本阶段不生成段或错误 |

闭合 polyline 若最后顶点已等于第一顶点，不再重复添加零长度闭合段；中间重复点绝不删除，由验证器报告。闭合标志导致的概念闭合是源语义，不是自动拼接。验证器也允许单独验证“声明闭合但段链未闭合”的 `CadContour`，并报告 `CONTOUR_NOT_CLOSED`；这覆盖损坏或其他适配器提供的不一致中间结果。

DXF bulge 按 `bulge = tan(includedAngle / 4)` 转换。bulge 为零生成直线；正值生成逆时针圆弧，负值生成顺时针圆弧。转换保留精确端点，中心和半径由弦长及包含角推导；非零 bulge 配合小于零长度容差的弦会报告 `CONTOUR_BULGE_CHORD_TOO_SHORT`，而不会改动原始顶点。

`ARC` 的方向为逆时针，端角跨越 0 度时仍由归一化角区间判断，而不是比较 `end > start`。圆弧范围包含位于弧上的 0、90、180、270 度极值。所有角度使用度。

## 容差、面积和方向

`CadGeometryTolerance` 集中声明并校验所有非负有限容差。默认值为：点相等 `0.000001 m`、零长度 `0.000001 m`、平面偏差 `0.000001 m`、近零面积 `0.000000000001 m²`，以及交叉检测每个圆弧最多 32 条采样弦。容差仅用于判定，绝不修改坐标。

线段长度和轮廓范围按二维计算。线性段的有向面积贡献使用叉积；圆弧使用 Green 公式的解析积分，因此面积包含圆弓部分而不是弦的近似。圆的有向面积为 `πr²`，约定其方向为 CounterClockwise。大于面积容差为 CounterClockwise，小于负面积容差为 Clockwise，其余为 Undefined。

## 轮廓构建与验证

只构建单一源实体明确声明闭合的候选：闭合 LWPOLYLINE 和 Circle。LINE、ARC、开放 LWPOLYLINE 均进入 `OpenSegments`；不同实体之间不会连接。轮廓稳定标识仅取源序号和轮廓内段序，例如 `contour:0003`。

验证逐候选进行，错误轮廓不会使整个文档失败：

- 段不足：`CONTOUR_SEGMENT_COUNT_INSUFFICIENT`
- 零长度段：`CONTOUR_ZERO_LENGTH_SEGMENT`
- 相邻段不连续：`CONTOUR_SEGMENTS_DISCONNECTED`
- 声明闭合但未闭合：`CONTOUR_NOT_CLOSED`
- 面积过小：`CONTOUR_AREA_TOO_SMALL`
- 非共面：`CONTOUR_NON_PLANAR`
- 自交：`CONTOUR_SELF_INTERSECTION`

自交检测对线-线、线-弧、弧-弧使用受限采样弦。采样仅供验证，不作为官方轮廓数据、不写回段、也不宣称 CAD 精确布尔几何。只排除相邻段或首尾段的唯一合法共享端点；T 型触碰、端点落在非相邻段内部及共线重叠仍是自交。Circle 自身不视为自交。复杂度为段生成、连续性和面积 O(n)，自交 O(n²)，适用于本阶段公开合成样本和受控 POC，不提供空间索引或无界采样。

`CadContourBuildStatus.Succeeded` 表示候选均有效或只有开放段；`PartiallySucceeded` 表示至少一个候选无效，且仍完整保留全部候选与诊断（若有有效候选也保留它们）；`Failed` 仅用于不可用输入或构建器内部不可恢复失败。没有闭合候选但存在开放段是成功结果。

诊断使用稳定的全大写 ASCII 代码、受控英文说明和匿名 subject（如 `contour:0003` 或 `source-order:12`）。不包含绝对路径、客户名、真实图层/Block 名称、设备编码、坐标负载或第三方异常文本。SB-06 不修改 `JobReport` 和既有 `SceneDiagnostic` 的 JSON 形状。

## 测试与证据

先以 Domain 测试定义容差、段范围、bulge（0、正负 1、很小正负值）、圆弧跨零、闭合矩形、反向方向、含 bulge 的闭合线、Circle、开放段、INSERT、每种无效轮廓及部分成功。Cad 测试只证明公开合成 DXF 能经既有提取与标准化进入本阶段；真实厂房图纸和敏感配置不提交。

完成时运行 Domain、Cad、Application、全 Solution 测试和构建，并检查 `git diff --check`、UTF-8 严格解码、文档 JSON 和敏感信息扫描。性能测量、自动修复和 DWG 转换结论不属于本阶段证据。
