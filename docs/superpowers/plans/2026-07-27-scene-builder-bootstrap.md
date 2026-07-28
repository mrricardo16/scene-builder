# IDTS Scene Builder Bootstrap Implementation Plan

**Goal:** 创建可构建、可测试的 .NET 8 CLI 项目骨架，并交付首版可用的 `scene-builder doctor` 与稳定的领域契约。

**Global Constraints:** 所有文本文件 UTF-8；DXF 是首条可验收路径；DWG 和 3D Tiles 仅保留可替换边界与诊断，不能声明已支持；任务目录必须隔离；外部工具缺失只报告不可用。

### Task 1: Repository documentation and solution skeleton

- Create the solution, eight source projects and two test projects.
- Add root encoding, repository, execution, and output-directory rules.
- Add Chinese UTF-8 documentation under `docs/`, including the project charter and SB-00 task card.
- Verify `dotnet build` and `dotnet test` run successfully with the empty baseline.

### Task 2: Domain contracts and report models

- Create source-format, unit, bounds, diagnostic, `CadDocumentModel`, `SceneDraft`, job-layout, and Tiles-converter contracts in the Domain project.
- Write unit tests first for stable job directories, immutable model defaults, and report serialization shape.
- Keep contracts independent of ACadSharp, Blender, a selected Tiles converter, and current IDTS manifests.

### Task 3: Doctor inspection service and CLI

- Write unit tests first for .NET runtime, configured executable, and missing-tool inspection.
- Implement strongly typed options, probe abstractions, a doctor service, structured JSON report, and a `scene-builder doctor` command.
- Configure the CLI to print a human-readable summary and write `doctor-report.json` when `--output` is provided; missing optional tools must not return an error.

### Task 4: CAD and pipeline adapter boundaries

- Add DXF/DWG inspection interfaces and an explicit unsupported DWG probe result; no DWG conversion implementation.
- Add Blender and Tiles interfaces with process request/result contracts and cancellation tokens.
- Write contract tests proving the default Tiles implementation reports `NotConfigured` without pretending conversion succeeded.
- Document the SB-01 through SB-09 gates, support matrix, SceneDraft contract, rules, partitioning, Tiles, and performance baseline.

### Task 5: Verification and final review

- Run `dotnet build`, `dotnet test`, `scene-builder doctor`, and `scene-builder doctor --output <temporary job folder>`.
- Check output paths are contained beneath the requested job directory and UTF-8 documentation remains valid.
- Review the complete branch diff, then record exact commands and results in the handoff.
