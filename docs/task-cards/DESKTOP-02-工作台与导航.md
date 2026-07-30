# DESKTOP-02：项目导入与分析页面

## 目标

实现工作台、项目导入和分析页面，覆盖导入 → 受控复制 → Analyze → 分析摘要。

## 范围与退出

显示单位、Bounds、Layer、Block、实体、Xref、代理对象、轮廓、分类和诊断。DXF 可按配置进入 Analyze；DWG 未配置或验证中显示明确原因而不启动作业。不得在 ViewModel 直接解析 CAD 或启动 Blender。能显示真实 Analysis 状态并保留无敏感信息诊断即退出。
