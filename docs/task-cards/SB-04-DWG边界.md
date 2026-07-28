# SB-04：DWG 边界闸门

## 目标

在未验证 DWG 实现前，显式报告不支持而非暗中转换。

## 通过闸门

- `IDwgInspector` 使用带 `CancellationToken` 的探测接口。
- `UnsupportedDwgProbe` 返回 `DwgProbeStatus.Unsupported` 和 `DWG_UNSUPPORTED` 诊断。
- 产品路径不解析 DWG、不运行转换程序、不生成 DWG 输出。
- 仅在获得单独授权时，可在私有、隔离、限时的 POC 中调用候选读取器或受控离线转换器；POC 的成功或失败均不得改变产品支持状态，且不得提交源图纸、转换产物或敏感日志。

## 当前状态

明确未支持；当前私有 POC 的直接读取和 Core Console 转换均在 30 秒内超时，结论为 `continue-validation`。任何“DWG 已支持”声明必须以独立实现、真实样本和回归测试为前提。

## 追溯

| 输入 | 稳定契约 | 测试 | 产物 | 证据 |
| --- | --- | --- | --- | --- |
| DWG 样本元数据与探针配置 | `IDwgInspector`、`DwgProbeStatus` | 未配置、取消、许可拒绝 | 探针报告 | [POC 决策记录](../工具选型与POC决策记录.md)；通过前为 Unsupported |
