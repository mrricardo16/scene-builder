# DESKTOP-05：任务历史与本地存储

## 目标

实现规划中的 `%LocalAppData%/SceneBuilder` JobId 隔离目录、UTF-8 JSON 元数据和本地任务历史；首版不使用数据库。

## 验收

- 每作业拥有 `input`、`config`、`work`、`artifacts`、`logs` 和 `job.json`；相对路径不得逃逸根目录。
- 设置与作业 JSON 原子写入、严格读取；损坏记录可诊断且不阻断其他页面。
- 重启只恢复历史，不自动重跑异常中断作业；JSON 不含凭据或客户数据。

## 非目标

不替换 `JobReport v0`，不引入数据库、云同步、保留期删除或导出功能。
