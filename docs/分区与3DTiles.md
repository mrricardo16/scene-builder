# 分区与 3D Tiles

## 分区边界

分区策略属于流水线用例层：它根据 `SceneDraft` 的节点范围和业务规则生成候选分区。领域层只保存稳定的节点、范围和诊断，不依赖 Tiles 格式、转换器命令行或 Blender。

## 当前 Tiles 状态

`ITilesConverter` 接收 `TilesConversionRequest` 并返回 `TilesConversionResult`。默认 `NotConfiguredTilesConverter` 的固定行为是：

- 状态为 `NotConfigured`；
- `OutputPath` 为 `null`；
- 写入代码为 `TILES_NOT_CONFIGURED` 的警告诊断；
- 不创建文件、不运行进程、不报告转换成功。

`ITilesProcessRunner` 仅定义未来受控外部进程的请求/结果边界。配置具体转换器、参数白名单、超时、产物校验和真实样本验收前，不得替换默认状态。
