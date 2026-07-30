# CORE-02：CAD 导入分析

## 目标

将受控 CAD 输入转换为 `CadImportAnalysisResult`，仅完成解析、标准化、分析和诊断，不启动 Blender。

## 前置、输入与契约

依赖 CORE-01 和对应输入支持闸门。输入经 `ICadInputAdapter` 或等价契约进入；输出包含输入类型/版本、单位、Bounds、Layer、Block、实体、未支持实体、Xref、代理对象、轮廓、修复建议、分类、未分类、资产候选和诊断。

## 范围与非目标

复用现有 DXF 组件和 Domain 契约，隔离 DWG 的直接或受控中间 DXF 路径。不得静默猜单位、修改原始 CAD、启动 Blender，或把未通过闸门的 DWG 伪装为成功。

## 验证与退出

验证 DXF 分析无 Blender 进程；DWG 未配置和验证中均返回明确状态；诊断不泄露敏感内容。退出时分析结果可稳定供 CORE-03 使用。
