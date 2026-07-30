# LARGE-04：Viewer 大场景验收

## 目标

验证受控 Viewer 对已验证大型厂区 tileset 的加载、浏览、资源释放和失败处理。

## 前置、输入与契约

依赖 LARGE-00 至 LARGE-03 与 DESKTOP-00 的 Viewer 技术结论。输入只能是当前 Build Job 根内已验证的相对 tileset/内容 URI；记录 Viewer 名称、版本、环境、加载时间、内存、交互、错误和释放证据。

## 范围与非目标

验证首次加载、区域定位、平移缩放、长时间浏览、关闭/取消、错误、受控回退和资源释放。不得接受任意 URL、任意本机路径或脚本；不得在此卡直接接入 IDTS/Cesium 产品、地理配准或远端发布。

## 验证与退出

对 Small、Medium、Large 与 Stress/Boundary 样本执行验收；性能与内存必须同时满足已确认预算。退出时可声明受控 Viewer 对明确样本范围已验证，不能外推为通用 Viewer 或 IDTS 接入。
