# DESKTOP-04：规则、分类与资产绑定

## 目标

提供 Layer、Block、分类、未分类对象、冲突与显式资产绑定工作区。

## 范围与退出

UI 生成或修改现有严格规则契约并复用 `CadRuleEngine`，展示而不重写规则排序。资产使用 Asset Catalog、显式 AssetId、确定性 Binding 和既有安全读取边界。不得允许脚本匹配、BlockName 猜文件名或绕过规则引擎。冲突、未分类和缺失资产行为可验证即退出。
