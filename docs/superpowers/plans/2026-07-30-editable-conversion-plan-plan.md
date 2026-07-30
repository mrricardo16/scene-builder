# Editable Conversion Plan Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create, revise, validate, and freeze deterministic conversion plans from validated CORE-02 analysis artifacts.

**Architecture:** Application owns strict artifact reading, immutable plan records, lifecycle validation, and atomic JSON publication. Composition exposes the same service to CLI and future Avalonia; no Build-layer dependency is introduced.

**Tech Stack:** .NET 8, System.Text.Json, SHA-256, xUnit.

## Global Constraints

- Use UTF-8, controlled output-root paths, staging publication, strict reread, and `SceneDiagnostic`.
- Do not read DXF or invoke SceneDraft, Blender, Package, Tiles, or any process.
- Keep Build capabilities Planned; preserve existing artifact enum values.

---

### Task 1: Plan contracts and Draft creation

**Files:** `src/SceneBuilder.Application/ConversionPlanContracts.cs`, `src/SceneBuilder.Application/ConversionPlanService.cs`, `tests/SceneBuilder.Application.Tests/ConversionPlanServiceTests.cs`

- [ ] Write `CreateDraftAsync_WithValidatedAnalysis_PublishesRevisionOne` and run the filtered test; expect missing service/contracts.
- [ ] Add `ConversionPlanDraft`, request/result records, strict Analysis identity reader, deterministic PlanId/content hash, and atomic `plans/revision-0001/plan-draft.json` publication.
- [ ] Run the filtered test; expect pass.

### Task 2: Revision, validation, and frozen snapshot

**Files:** `src/SceneBuilder.Application/ConversionPlanService.cs`, `tests/SceneBuilder.Application.Tests/ConversionPlanServiceTests.cs`

- [ ] Write a lifecycle test for revision 2, output validation, and freeze; run it before implementation.
- [ ] Add no-overwrite revision publishing, unit/transform/height/repair/output validation, `validation.json`, and frozen content-id-bound snapshot publication.
- [ ] Run lifecycle tests and verify revision 1 remains unchanged.

### Task 3: Host and CLI

**Files:** `src/SceneBuilder.Composition/SceneBuilderHost.cs`, `src/SceneBuilder.Composition/SceneBuilderComposition.cs`, `src/SceneBuilder.Cli/PlanCommand.cs`, `src/SceneBuilder.Cli/CliCommandParser.cs`, `src/SceneBuilder.Cli/SceneBuilderCliApplication.cs`, `src/SceneBuilder.Cli/CliOutputWriter.cs`

- [ ] Register the service explicitly and expose `plan create`, `plan validate`, and `plan freeze` text/JSON commands.
- [ ] Update capability registry without changing Build/DWG states.
- [ ] Run build and CLI manual checks.

### Task 4: Documentation and final verification

**Files:** README and requested CORE-03 product/CLI/task documents.

- [ ] Document exact Plan support and CORE-04 boundary.
- [ ] Run build, full tests, UTF-8 strict decode, `git diff --check`, deterministic CLI lifecycle, and no-three-dimensional-call review.
