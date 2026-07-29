# SB-09：厂房语义对象与 SceneDraft

## 已支持

- 从可信 `CadClassificationResult`、最终 `CadContourDocument` 和标准化 Insert 构造 Wall、Floor、Column、Road、StaticFacility、DynamicEquipment；
- 使用与 SB-08 一致的 Contour、OpenSegment、Insert Subject Id，验证来源、分类兼容性、轮廓有效性、Insert 实例和匹配证据；
- 将语义对象映射为确定性 `SceneNode`：程序化静态几何、静态资产引用、动态资产引用；
- 生成内部 `SceneDraft`，其语义对象、节点、跳过 Subject 与诊断均按 Ordinal 稳定排序；
- Application 可显式选择 SB-07 的修复后轮廓或原始轮廓，不重复规则、验证或修复；
- 公开合成 DXF 已覆盖完整 DXF→分类→SceneDraft 链路。

## 受控行为

有效 Contour 可生成 Wall、Floor、Column、Road；OpenSegment 只可生成 Wall 基线或 Road 中心线；实际 Insert 只可生成静态设施或动态设备。`unclassified` 不生成对象或节点。来源缺失、伪造 Subject、重复分类结果、非有限规则默认值和不兼容来源会产生匿名稳定诊断并跳过该对象；来源稳定 Id 重复或分类失败使草稿失败。

`GeometryDefaults` 当前只含 `HeightMeters`。Wall 与 Column 仅传递配置中可用的正有限高度；缺失时不会猜测数值，保留语义对象并标记为部分成功。Floor、Road、StaticFacility、DynamicEquipment 不把高度解释成厚度、宽度、缩放或资产路径。

## 非目标

SB-10 已在不改变本卡语义契约的前提下消费 SceneDraft 生成最小 Blender GLB 草模；本卡自身仍不实现 Mesh、墙体拉伸、地面三角剖分、柱体生成、道路 Buffer、门窗洞口、屋顶、材质、Blender、GLB、分区 GLB、3D Tiles、DWG、ezdxf、Xref 内容加载、代理对象语义转换、外部进程、HTTP、数据库、IDTS 集成或 JobReport v0 修改。

系统已经能够将可信二维 CAD 分类结果转换为可供三维建模阶段消费的内部 SceneDraft；它尚未生成三维厂房。
