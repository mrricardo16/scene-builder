# Scene Builder Desktop UI 页面设计

## 工作流和页面

Desktop 的主工作流是：1. 导入，2. 分析，3. 调整，4. 预览，5. 生成，6. 结果。旧的“选择文件 → 选择规则 → 确认目录 → 预检”四步向导不足以表达分析结果、可编辑计划和冻结构建，不能再作为主流程。

| 页面/工作区 | 责任 | 当前状态 |
| --- | --- | --- |
| 工作台 | 最近项目、诊断摘要、导入入口 | Planned |
| 项目导入 | 选择 DWG/DXF，复制到受控项目目录 | Planned |
| 分析工作区 | 显示输入、单位、Bounds、Layer、Block、实体、诊断 | Planned |
| 规则与分类 | 编辑现有严格规则契约，查看冲突和未分类对象 | Planned |
| 几何参数 | 仅编辑现有 `HeightMeters` 等已支持语义 | Planned |
| 资产绑定 | 显式 AssetId 与确定性 Binding | Planned |
| 输出配置 | GLB、Package、Tiles 与现有输出参数 | Planned |
| 生成进度 | 冻结后 Build 的阶段、进度、取消和日志 | Planned |
| 项目/作业历史 | Project、Analysis、Plan Revision、Build Job、Artifact | Planned |
| 系统检查和设置 | 工具配置与 Doctor | Planned |
| GLB / 3D Tiles 预览 | 仅加载已验证产物 | Planned |

## 工作台布局

顶部工具栏提供导入、重新分析、保存计划、验证计划、生成和取消。左侧是 Layer、Block、分类、未分类对象和诊断树；中央是二维 CAD 预览，可切换粗略三维预览；右侧显示当前对象属性、分类参数、规则覆盖、资产映射及位置/旋转/缩放；底部显示诊断、冲突、修复建议和作业日志。

分析阶段优先使用 Avalonia/Skia 二维 CAD 预览，以支持 Layer 可见性、Bounds、轮廓、分类着色、对象选择和未分类定位，且不依赖 Blender。GLB 预览计划使用离线 WebView + Three.js 或受控外部查看器；3D Tiles 预览需要独立 Viewer POC，不能因底层 tileset 已生成而宣称 UI 已支持。

## 输入和状态

文件选择器可以列出 DWG 与 DXF，但行为必须由支持状态决定：DXF 已配置时可进入 Analyze；转换器未配置的 DWG 显示“DWG 转换器未配置”；闸门未通过的 DWG 显示“DWG 支持仍在验证”。后二者均不得创建或启动作业。

UI 只发送 Application 请求并显示结果，不重写分类排序、不猜测单位、不修改原始 CAD，也不让 Blender 读取可变 ViewModel。状态必须区分分析、可编辑计划、冻结计划、构建和已验证产物；错误、取消或未配置工具不得显示为成功。
