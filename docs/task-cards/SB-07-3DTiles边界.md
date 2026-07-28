# SB-07：3D Tiles 边界闸门

## 目标

防止在没有配置转换器时错误报告 3D Tiles 转换成功。

## 通过闸门

- `ITilesProcessRunner` 定义受控进程边界并接受取消令牌。
- `NotConfiguredTilesConverter` 返回 `NotConfigured`、空输出路径和 `TILES_NOT_CONFIGURED` 诊断。
- 默认路径不创建产物，也绝不返回 `Succeeded`。

## 当前状态

默认未配置行为已有自动化契约测试；未提供真实 3D Tiles 转换。

## 追溯

| 输入 | 稳定契约 | 测试 | 产物 | 证据 |
| --- | --- | --- | --- | --- |
| 分区产物和 Tiles 请求 | `ITilesConverter`、转换结果 | 未配置、失败隔离、坐标抽样 | Tiles 报告及未来 tileset | [POC 决策记录](../工具选型与POC决策记录.md)；通过前为 NotConfigured |
