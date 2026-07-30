# DESKTOP-08：Windows 发布

## 目标

交付未来 Windows 10/11 的 `win-x64` self-contained ZIP 发布方案。

## 验收

- 在干净 Windows 环境解压并启动，检查工作台、Doctor、DXF-only 提示、取消和预览/回退。
- 发布包不含 Blender、客户样本、令牌、作业日志或本地数据；运行时数据写入受控本地作业根。
- 记录 runtime、包大小、校验和与脱敏验证证据；签名策略在发布实施前单独确认。

## 非目标

不实现自动更新、在线安装器、云发布或 3D Tiles 功能。
