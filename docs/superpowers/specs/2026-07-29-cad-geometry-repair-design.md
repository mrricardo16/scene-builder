# SB-07 CAD 几何容差、散线连接与受控修复设计

## 目标与管线

SB-07 在 SB-06 的 `CadContourDocument` 之后运行，建立可审计且确定的纯内存管线：

```text
CadContourDocument + CadGeometryRepairPolicy
  -> CadGeometryRepairAnalyzer
  -> CadGeometryRepairPlan
  -> CadGeometryRepairApplier
  -> CadContourValidator reuse
  -> CadGeometryRepairResult
```

分析不修改输入；计划不执行修改；应用只生成独立的新段、新轮廓和审计动作。`OriginalDocument` 保持原对象和原段不变。SB-06 的 `CadContourValidator` 负责修复后连续性、闭合、零长度、面积、非平面、自交、范围、面积和方向的复验；本阶段不复制另一套轮廓验证算法。

所有距离为标准化局部坐标的米，所有连接判断仅使用 XY，Z 差必须不大于 `MaximumElevationDifferenceMeters`。Domain 不引用 ACadSharp、文件、进程、HTTP、数据库或 IDTS；不修改 JobReport v0、DWG 能力状态或任何公开报告 wire shape。

## 策略和稳定标识

`CadGeometryRepairPolicy` 集中声明 EndpointSnap、Bridge、重复、共线距离、角度和 Z 容差，以及每一类受控动作的开关。每个数值必须为有限非负数，角度上限为 180 度。默认策略保守：小端点吸附 `0.002 m`、小缺口桥接 `0.02 m`、重复/共线/高程 `0.000001 m`、共线角度 `0.1°`；默认不跨图层。

`CadCurveSegment2.Id` 稳定地由 `source-order` 与 `segment-order` 生成，例如 `segment:000012:000003`，不含路径、图层、客户或随机值。生成桥线和合并线使用其 `RepairAction.Id` 派生的段 ID。动作和计划 ID 由稳定排序后的动作类型、来源段 ID 与端点角色（SS/SE/ES/EE）等最小必要几何角色决定；不使用 GUID、内存地址、完整坐标或遍历偶然顺序。

## 分析规则

- 默认只在相同源图层内比较；跨图层候选写入 `REPAIR_CROSS_LAYER_BLOCKED`，不会成为动作。
- 不比较同一段的两个端点；相同 XY 点不产生 Snap。
- XY 距离 `(0, snap]` 为 `SnapEndpoint` 候选，目标点固定为两点中点，置信度 High；本版本只吸附直线段端点，避免隐式重定义圆弧半径和角度。
- XY 距离 `(snap, bridge]` 为 `BridgeGap` 候选，生成新的直线而不移动原端点，置信度 High。圆弧可参与桥接端点，但不移动圆弧本身。应用前必须拒绝与第三方开放直线、开放圆弧、已有段轮廓或圆轮廓相交的桥线。
- 只识别直线的完全同向和反向重复；保留 ordinal 更小的段 ID，动作分别为 `RemoveExactDuplicate`、`RemoveReverseDuplicate`，置信度 Deterministic。部分重叠、平行不重合和圆弧重复只报告为不支持，不被删除。
- 同层、连续、无分支、夹角与偏离均在容差内的两条直线可生成 `MergeCollinearSegments`。合并不跨越 T 分支或与其他动作竞争。
- 链按段 ID 排序开始，只有唯一的后继才延长；反向只使用段的几何副本作为内部遍历证据，不改变原始段，也不写入最终 `AppliedActions`。`AllowSegmentReversal=false` 时需要反向的链保持开放。度数大于 2 是 `REPAIR_CHAIN_BRANCHING_CONFLICT`，不做组合搜索。
- 闭环只从唯一连续链发现；不枚举任意图组合、最优路径或复杂网络。

候选动作以确定顺序排序。若同一端点需要吸附到多个目标、同一段被删除且合并、同一段进入互斥闭环、桥接与重复删除竞争，计划为 `HasConflicts` 并含 `REPAIR_ACTION_CONFLICT`；应用器不会静默选择任意一项。

## 应用、回退和状态

自动应用只允许 `Deterministic` 和 `High`，并要求相应 policy 开关。Low 永不自动应用；Medium 预留但默认关闭。应用器从原始开放段复制工作集，按稳定顺序应用允许且非冲突动作；每项 Applied 或 Skipped 均进入结果。桥接如果形成自交、无效闭合轮廓或使原先有效轮廓无效，则不保留该项修改并记录 `REPAIR_RESULT_INVALID` / `REPAIR_ACTION_SKIPPED`。

`NoChangesRequired` 表示没有可应用动作，仍可输出从原始散段发现的无修改闭环；`Succeeded` 表示所有计划动作被安全应用；`PartiallySucceeded` 表示保留了部分安全动作、冲突或跳过项；`Failed` 表示输入/计划不可用或没有可安全产出修复结果。无效候选绝不伪装为成功。

## 边界、性能和隐私

端点候选和重复检查允许 O(n²)，链构建 O(n + e)。输入受公开合成样本和受控 POC 规模限制；不引入 R-tree、NetTopologySuite、图算法库或无界组合搜索。诊断全为 ASCII 机器码，且仅使用匿名 `segment:`、`repair-action:`、`chain:` subject 风格标识；不写绝对路径、真实图层/Block 名称、客户数据、完整坐标负载或第三方异常文本。

不实现复杂图网络求解、默认跨图层修复、大缺口补线、自交自动修复、Polygon 布尔运算、洞/外环关系、规则/语义识别、SceneDraft、Blender、GLB、Tiles、DWG、ezdxf、Xref 或代理对象转换。

## 测试

先写并运行失败测试，再实现最小功能。覆盖策略校验、空集合、稳定 ID、同层与跨层、Z 限制、Snap、Bridge、同向/反向重复、部分重叠、共线合并、无分支链、闭环、分支冲突、修复后 SB-06 复验、原始对象不变、确定性和部分成功。公开 fixture 仅使用合成图层和简单米制坐标。
