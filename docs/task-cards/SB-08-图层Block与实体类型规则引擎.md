# SB-08：图层、Block 与实体类型规则引擎

## 目标

以项目级规则对可信二维 Contour、OpenSegment 和 Insert 进行确定性语义分类。输入来自标准化几何、SB-06 轮廓及可选 SB-07 修复结果；输入不被修改。

## 已支持

- `1.0` 规则 JSON 的严格 DTO 映射和完整 RuleSet 验证；
- Layer/Block 的精确及 `*`、`?` 通配匹配，OrdinalIgnoreCase；
- EntityType 精确前置过滤；
- 冻结 matchRank、priority、冲突和同分类重复规则；
- Contour、OpenSegment、Insert 的稳定 Subject 与分类结果；
- 无效轮廓保留但保持 `unclassified`；
- `RULE_CONFIG_INVALID`、`RULE_CONFLICT`、`RULE_DUPLICATE_MATCH` 内部诊断。

## 非目标

本卡本身不创建厂房三维语义对象、SceneNode、SceneDraft、墙体/地面/门窗几何、Blender、GLB、3D Tiles、DWG、ezdxf、Xref、HTTP API、数据库或公开分类报告；不修改 JobReport v0。SB-09 已在不改变本卡分类含义的前提下消费其内部结果，仍不生成三维模型。

## 门禁

规则顺序和 Subject 输入顺序不得改变结果。不同分类的并列匹配不可静默选择；无效规则集合不可执行部分规则。规则与分类结果仅为内部契约，公开报告必须由后续脱敏适配器处理。
