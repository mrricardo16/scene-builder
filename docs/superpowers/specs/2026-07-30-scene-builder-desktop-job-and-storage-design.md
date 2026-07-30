# Scene Builder Desktop 作业与本地存储设计

## 目录和对象边界

首版使用严格 UTF-8 JSON 和目录存储，不引入数据库。应用根为概念性的 `%LocalAppData%/SceneBuilder`，不得硬编码机器绝对路径或把运行时文件写回仓库。

```text
%LocalAppData%/SceneBuilder/
    settings.json
    projects/
        projectId/
            project.json
            source/
            analysis/
            plans/
                revision-0001.json
            jobs/
                jobId/
                    request.json
                    frozen-plan.json
                    work/
                    artifacts/
                    logs/
                    job.json
```

Project、Analysis、Plan Revision、Build Job 与 Artifact 是独立对象：源输入只读保存；分析可重建；计划修订永不覆盖旧版本；Build 只使用 `frozen-plan.json`；不同 Build Job 的工作目录和产物隔离；重新生成一定创建新作业。

## 保存、恢复和安全

`settings.json` 只保存非敏感偏好和经校验的工具路径。所有 JSON 使用严格 UTF-8、临时文件加原子替换；读取拒绝未知/不合法结构并产生可读诊断。所有相对路径必须解析后仍位于相应根目录内，拒绝空 ID、`..`、路径分隔符、Windows 设备名和越界目标。

启动时可恢复项目和终态作业历史；异常中断的非终态 Build 记录为需要诊断的失败/中断状态，绝不自动重跑。`job.json` 是 Desktop 元数据，不替换或镜像 Domain `JobReport v0`。

## 调度

`IDesktopJobRunner` 计划为单例、单并发协调器：先持久化 queued，再调用统一 Application 服务，最后写入终态快照。`JobStatus`、`JobStage` 与 `JobProgress` 是三个独立模型；未知百分比不伪造完成度。取消只协作式传播 `CancellationToken`，窗口关闭先确认再请求取消并等待受控收敛。
