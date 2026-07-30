# CAD-DWG-01：DWG 适配与支持闸门

## 目标

实现受控 `ICadInputAdapter` 的 DWG 路径，并证明其满足正式支持闸门。

## 前置、输入与契约

输入为匿名真实 DWG、已选择并许可合规的转换器或直接读取器、显式工具版本和受控作业目录。DWG Adapter 的结果必须保留转换器版本、许可结论、中间 DXF、源文件未修改证明、取消、超时、Xref、代理对象和缺失工具诊断。

## 范围与非目标

可采用直接解析或 DWG→中间 DXF→DXF Adapter→Analyze。不得修改源 DWG，不得把当前 Core Console POC 或 ezdxf POC 宣称为产品实现，也不得把转换器未配置或验证中显示为成功。

## 验证与退出

验证许可、安装、版本、重复性、取消、超时、Xref、代理对象、真实样本回归和工具缺失路径。闸门全部通过前，DWG 必须保持 Unsupported/ContinueValidation。
