# CORE-01 Shared Application Host and CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reusable application composition root, shared execution contracts, a deterministic capability registry, and a testable CLI framework without implementing conversion stages.

**Architecture:** `SceneBuilder.Application` owns pure, strongly typed public contracts and side-effect-free validators. `SceneBuilder.Composition` owns the default object graph for Doctor and the capability registry; `SceneBuilder.Cli` owns command parsing and output while `Program` only supplies process concerns. No project introduces a dependency back to Composition or CLI.

**Tech Stack:** .NET 8, xUnit 2.4.2, System.Text.Json; no new NuGet packages.

## Global Constraints

- Keep modified text UTF-8 and preserve Chinese wording.
- Do not change Domain, CAD, Blender, Pipeline, Tiles, JobReport v0, Scene Package or Tileset contracts.
- Do not implement Analyze, Plan, Build, convert, Avalonia, DWG, Viewer or HLOD.
- Runtime artifacts require a caller-provided output root; pure validation creates no directories.
- Keep `doctor` text behavior and existing exit-code behavior compatible.
- Do not use `git add .`, push, branch, merge, rebase, reset, amend or pull.

---

### Task 1: Freeze documentation and product boundary

**Files:**
- Modify: `docs/task-cards/CORE-01-统一应用层转换入口.md`
- Modify: `docs/产品目标与能力状态.md`
- Modify: `docs/路线图与任务卡映射.md`
- Modify: `docs/scene-builder-avalonia-desktop-roadmap.md`
- Modify: `README.md`
- Create: `docs/CLI退出码与输出契约.md`
- Create: `docs/superpowers/specs/2026-07-30-shared-application-host-and-cli-design.md`

- [x] State that CORE-01 delivers only common contracts, host, registry and CLI framework.
- [x] State that `doctor` and `capabilities` are host commands, while real Analyze, Plan and Build remain CORE-02 to CORE-04.
- [x] Define exit codes 0, 2, 3, 4 and 5 and deterministic JSON requirements.

### Task 2: Add Application contracts using TDD

**Files:**
- Create: `src/SceneBuilder.Application/Hosting/SceneOperationContracts.cs`
- Create: `src/SceneBuilder.Application/Hosting/SceneOperationContractValidator.cs`
- Create: `src/SceneBuilder.Application/Hosting/SceneCapabilityRegistry.cs`
- Create: `src/SceneBuilder.Application/Hosting/OutputRootPolicy.cs`
- Create: `tests/SceneBuilder.Application.Tests/SceneOperationContractTests.cs`
- Create: `tests/SceneBuilder.Application.Tests/SceneCapabilityRegistryTests.cs`
- Create: `tests/SceneBuilder.Application.Tests/OutputRootPolicyTests.cs`

**Interfaces:**
- Produces `SceneApplicationOperation`, `SceneOperationStatus`, `SceneWorkflowPhase`, `SceneOperationProgress`, `SceneArtifactDescriptor`, `SceneOperationResult` and `ISceneOperationHandler<TRequest,TResult>`.
- Produces `SceneCapabilityState`, `SceneCapability` and `ISceneCapabilityRegistry`.
- Produces `IOutputRootPolicy.Validate(string)` without filesystem creation.

- [x] Write failing tests for progress limits, StageCode ASCII, artifact path containment, result status/artifact invariants, capability ordering/uniqueness, and output root validation.
- [x] Run `dotnet test tests/SceneBuilder.Application.Tests/SceneBuilder.Application.Tests.csproj --no-restore` and record the expected missing-type failures.
- [x] Implement the smallest pure contracts and validators that satisfy the tests.
- [x] Re-run the project tests and keep all prior tests green.

### Task 3: Add composition root using TDD

**Files:**
- Create: `src/SceneBuilder.Composition/SceneBuilder.Composition.csproj`
- Create: `src/SceneBuilder.Composition/SceneBuilderHost.cs`
- Create: `src/SceneBuilder.Composition/SceneBuilderComposition.cs`
- Create: `tests/SceneBuilder.Application.Tests/SceneBuilderCompositionTests.cs`
- Modify: `SceneBuilder.sln`

**Interfaces:**
- Consumes Application Doctor contracts and `ISceneCapabilityRegistry`; consumes Infrastructure Doctor adapters.
- Produces `SceneBuilderHost` with `DoctorService` and `ISceneCapabilityRegistry` properties.

- [x] Write failing tests that create the Host, resolve Doctor and registry, and confirm default construction has no file or process side effect.
- [x] Run the existing Application test project and observe the missing-type failure.
- [x] Implement the manual, stateless factory and add only the actual Application/Infrastructure references.
- [x] Re-run Composition coverage and Application tests.

### Task 4: Refactor CLI using TDD

**Files:**
- Create: `src/SceneBuilder.Cli/SceneBuilderCliApplication.cs`
- Create: `src/SceneBuilder.Cli/CliCommandParser.cs`
- Create: `src/SceneBuilder.Cli/CliOutputWriter.cs`
- Modify: `src/SceneBuilder.Cli/Program.cs`
- Modify: `src/SceneBuilder.Cli/SceneBuilder.Cli.csproj`
- Create: `tests/SceneBuilder.Application.Tests/SceneBuilderCliApplicationTests.cs`

**Interfaces:**
- Consumes `SceneBuilderHost`, `DoctorCommandLineParser`, `DoctorReportWriter` and cancellation token.
- Produces `Task<int> RunAsync(string[] args, CancellationToken)` and deterministic capabilities output.

- [x] Write failing CLI tests for no argument/help/unknown command/capabilities text/capabilities JSON/invalid format/cancellation and doctor delegation.
- [x] Run the Application test project and observe the expected missing CLI type/member failures.
- [x] Implement parsing, output and exit-code mapping without exposing future conversion commands.
- [x] Re-run CLI and Doctor tests, including JSON parsing and no-file assertions.

### Task 5: Verify, audit and commit

**Files:**
- Verify all modified files above; no generated `bin/`, `obj` or runtime artifacts are staged.

- [x] Run each required project test, then `dotnet build SceneBuilder.sln --no-restore` and `dotnet test SceneBuilder.sln --no-build --no-restore`.
- [x] Run `dotnet run --project src/SceneBuilder.Cli -- --help`, `help`, `capabilities`, `capabilities --format json`, and `doctor`.
- [x] Independently review scope, host construction, Program composition, diagnostics, paths, capability states and dependencies.
- [x] Run UTF-8 strict decode, `git diff --check`, inspect the diff, stage explicit paths, and create `feat: add shared application host and CLI framework`.
