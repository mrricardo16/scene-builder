# SB-04：DWG 边界闸门

## 目标

在未验证 DWG 实现前，显式报告不支持而非暗中转换。

## 通过闸门

- `IDwgInspector` 使用带 `CancellationToken` 的探测接口。
- `UnsupportedDwgProbe` 返回 `DwgProbeStatus.Unsupported` 和 `DWG_UNSUPPORTED` 诊断。
- 不解析 DWG、不运行转换程序、不生成 DWG 输出。

## 当前状态

明确未支持；任何“DWG 已支持”声明必须以独立实现、真实样本和回归测试为前提。
