# DESKTOP-06：进度、取消、历史与恢复

## 目标

实现 Project、Analysis、Plan Revision、Build Job、Artifact 的隔离存储、进度显示、取消和恢复。

## 范围与退出

使用 `%LocalAppData%/SceneBuilder` 下的严格 UTF-8 JSON；每次 Build 创建新 Job 和独立 artifacts。区分 JobStatus、JobStage 与 JobProgress；取消协作传播并在关闭窗口前确认。不得使用数据库、覆盖历史、自动重跑中断作业、写回仓库或把 `JobReport v0` 当作 Desktop 元数据。路径越界、损坏 JSON、取消和恢复均验证通过即退出。
