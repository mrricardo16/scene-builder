[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BlenderPath,

    [string]$OutputRoot,

    [switch]$KeepOutputOnFailure
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$cliProject = Join-Path $repoRoot "src\SceneBuilder.Cli\SceneBuilder.Cli.csproj"
$fixture = Join-Path $repoRoot "tests\fixtures\synthetic\public-synthetic-closed-polyline.dxf"
$providedOutputRoot = -not [string]::IsNullOrWhiteSpace($OutputRoot)
$acceptanceRoot = if ($providedOutputRoot) { [IO.Path]::GetFullPath($OutputRoot) } else { Join-Path ([IO.Path]::GetTempPath()) ("scene-builder-core-04c-acceptance-" + [guid]::NewGuid().ToString("N")) }
$helperRoot = Join-Path ([IO.Path]::GetTempPath()) ("scene-builder-core-04c-helper-" + [guid]::NewGuid().ToString("N"))
$rulesPath = Join-Path $helperRoot "outline-rules.json"
$planHelperProject = Join-Path $helperRoot "PlanHelper.csproj"
$validationHelperProject = Join-Path $helperRoot "ValidationHelper.csproj"
$validationProgramPath = Join-Path $helperRoot "ValidationProgram.cs"
$failure = $true

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $lines = @(& $FilePath @Arguments 2>&1)
        [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($lines -join [Environment]::NewLine)
        }
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
}

function Invoke-CliJson([string[]]$Arguments, [int]$ExpectedExitCode = 0) {
    $result = Invoke-Native "dotnet" (@("run", "--project", $cliProject, "--no-build", "--") + $Arguments)
    Assert-Condition ($result.ExitCode -eq $ExpectedExitCode) ("Unexpected CLI exit code {0}; expected {1}. Output: {2}" -f $result.ExitCode, $ExpectedExitCode, $result.Output)
    try { $json = $result.Output | ConvertFrom-Json } catch { throw "CLI output was not valid JSON: $($result.Output)" }
    [pscustomobject]@{ ExitCode = $result.ExitCode; Output = $result.Output; Json = $json }
}

function Invoke-HelperJson([string]$Project, [string[]]$Arguments, [int]$ExpectedExitCode = 0) {
    $result = Invoke-Native "dotnet" (@("run", "--project", $Project, "--no-build", "--") + $Arguments)
    Assert-Condition ($result.ExitCode -eq $ExpectedExitCode) ("Unexpected helper exit code {0}; expected {1}. Output: {2}" -f $result.ExitCode, $ExpectedExitCode, $result.Output)
    try { $json = $result.Output | ConvertFrom-Json } catch { throw "Helper output was not valid JSON: $($result.Output)" }
    [pscustomobject]@{ ExitCode = $result.ExitCode; Output = $result.Output; Json = $json }
}

function Write-Utf8([string]$Path, [string]$Content) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

try {
    Assert-Condition (Test-Path -LiteralPath $BlenderPath -PathType Leaf) "BlenderPath must point to an existing file."
    Assert-Condition (Test-Path -LiteralPath $fixture -PathType Leaf) "The public synthetic fixture is missing."
    New-Item -ItemType Directory -Path $acceptanceRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $helperRoot -Force | Out-Null

    $version = Invoke-Native $BlenderPath @("--version")
    Assert-Condition ($version.ExitCode -eq 0) "Blender --version failed."
    $versionLine = ($version.Output -split "\r?\n" | Where-Object { $_ -match "^Blender \S+" } | Select-Object -First 1)
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($versionLine)) "Blender version output was not found."

    $smoke = Invoke-Native $BlenderPath @("--background", "--factory-startup", "--python-expr", "import bpy; print('SCENE_BUILDER_BLENDER_OK'); print(bpy.app.version_string)")
    Assert-Condition ($smoke.ExitCode -eq 0 -and $smoke.Output.Contains("SCENE_BUILDER_BLENDER_OK")) "Blender factory-startup smoke failed."

    $help = Invoke-Native "dotnet" @("run", "--project", $cliProject, "--no-build", "--", "help")
    Assert-Condition ($help.ExitCode -eq 0 -and $help.Output.Contains("Usage:")) "CLI help did not succeed."
    $doctorHelp = Invoke-Native "dotnet" @("run", "--project", $cliProject, "--no-build", "--", "doctor", "--help")
    $buildHelp = Invoke-Native "dotnet" @("run", "--project", $cliProject, "--no-build", "--", "build", "--help")
    Assert-Condition ($doctorHelp.ExitCode -eq 2 -and $doctorHelp.Output.Contains("Usage:")) "doctor --help behavior changed unexpectedly."
    Assert-Condition ($buildHelp.ExitCode -eq 2 -and $buildHelp.Output.Contains("Usage:")) "build --help behavior changed unexpectedly."

    $planSource = @'
using System.Text.Json;
using SceneBuilder.Application;
using SceneBuilder.Domain;
using SceneBuilder.Composition;

if (args.Length != 2) return 2;
var outputRoot = Path.GetFullPath(args[0]);
var previousPlanPath = Path.GetFullPath(args[1]);
var draft = JsonSerializer.Deserialize<ConversionPlanDraft>(await File.ReadAllTextAsync(previousPlanPath), BuildReadyPlanJson.Options) ?? throw new InvalidDataException();
var rules = new CadRuleSet
{
    ContractVersion = "1.0",
    Rules = [new CadClassificationRule
    {
        Id = "acceptance-wall",
        Enabled = true,
        Priority = 100,
        Match = new CadRuleMatch { Layer = "OUTLINE", EntityTypes = ["LWPOLYLINE"] },
        Classification = CadSemanticClassification.Wall,
        GeometryDefaults = new CadRuleGeometryDefaults { HeightMeters = 3.0 }
    }]
};
var result = await SceneBuilderComposition.CreateDefault().ConversionPlanService!.SaveRevisionAsync(new SaveConversionPlanRevisionRequest
{
    PreviousPlanPath = previousPlanPath,
    OutputRootDirectory = outputRoot,
    Draft = draft with
    {
        RuleSet = new ConversionPlanRuleSetSnapshotter().Create(rules),
        Outputs = new OutputConfigurationPlan { GenerateSingleGlb = true, GenerateScenePackage = true, Generate3DTiles = true }
    }
}, CancellationToken.None);
Console.WriteLine(JsonSerializer.Serialize(new { result.Status, Revision = result.Draft?.Revision, Diagnostics = result.Diagnostics.Select(item => item.Code).ToArray() }, BuildReadyPlanJson.Options));
return result.Status == SceneOperationStatus.Succeeded ? 0 : 1;
'@
    $planProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="__REPO_ROOT__\src\SceneBuilder.Application\SceneBuilder.Application.csproj" />
    <ProjectReference Include="__REPO_ROOT__\src\SceneBuilder.Composition\SceneBuilder.Composition.csproj" />
  </ItemGroup>
</Project>
'@
    $planProject = $planProject.Replace("__REPO_ROOT__", $repoRoot)
    Write-Utf8 (Join-Path $helperRoot "Program.cs") $planSource
    Write-Utf8 $planHelperProject $planProject
    $rulesJson = @'
{
  "contractVersion": "1.0",
  "rules": [{
    "id": "acceptance-wall",
    "enabled": true,
    "priority": 100,
    "match": { "layer": "OUTLINE", "entityTypes": ["LWPOLYLINE"] },
    "classification": "wall",
    "geometryDefaults": { "heightMeters": 3.0 }
  }]
}
'@
    Write-Utf8 $rulesPath $rulesJson
    $helperBuild = Invoke-Native "dotnet" @("build", $planHelperProject, "--nologo")
    Assert-Condition ($helperBuild.ExitCode -eq 0) ("Plan helper build failed: {0}" -f $helperBuild.Output)

    $analysis = Invoke-CliJson @("analyze", "--input", $fixture, "--rules", $rulesPath, "--unit", "meters", "--output", $acceptanceRoot, "--format", "json")
    Assert-Condition ($analysis.Json.status -eq "succeeded") "Analysis did not succeed."
    $planCreate = Invoke-CliJson @("plan", "create", "--analysis", (Join-Path $acceptanceRoot "analysis\cad-analysis.json"), "--output", $acceptanceRoot, "--format", "json")
    Assert-Condition ($planCreate.Json.status -eq "succeeded") "Plan create did not succeed."
    $revisionResult = Invoke-HelperJson $planHelperProject @($acceptanceRoot, (Join-Path $acceptanceRoot "plans\revision-0001\plan-draft.json"))
    Assert-Condition ($revisionResult.Json.status -eq "succeeded" -and $revisionResult.Json.revision -eq 2) "All-output plan revision was not published."
    $planPath = Join-Path $acceptanceRoot "plans\revision-0002\plan-draft.json"
    $validate = Invoke-CliJson @("plan", "validate", "--plan", $planPath, "--output", $acceptanceRoot, "--format", "json")
    Assert-Condition ($validate.Json.status -eq "succeeded" -and $validate.Json.validationStatus -eq "valid") "Plan validation did not succeed."
    $freeze = Invoke-CliJson @("plan", "freeze", "--plan", $planPath, "--output", $acceptanceRoot, "--format", "json")
    Assert-Condition ($freeze.Json.status -eq "succeeded" -and $freeze.Json.buildReadiness -eq "ready") "Plan freeze was not build-ready."
    $frozenPath = Join-Path $acceptanceRoot "plans\frozen\revision-0002.json"

    $blenderArgument = [IO.Path]::GetFullPath($BlenderPath)
    $build1 = Invoke-CliJson @("build", "--plan", $frozenPath, "--output", $acceptanceRoot, "--blender-path", $blenderArgument, "--format", "json")
    Assert-Condition ($build1.Json.status -eq "succeeded") "First real Blender build did not succeed."
    Assert-Condition (($build1.Json.outputs | Where-Object { $_.kind -in @("singleGlb", "scenePackage", "threeDTiles") } | Where-Object status -ne "succeeded").Count -eq 0) "First build did not succeed for all requested outputs."
    $build1Path = Join-Path $acceptanceRoot "builds\$($build1.Json.buildJobId)"
    $build1Hash = (Get-FileHash (Join-Path $build1Path "build-result.json") -Algorithm SHA256).Hash
    $build2 = Invoke-CliJson @("build", "--plan", $frozenPath, "--output", $acceptanceRoot, "--blender-path", $blenderArgument, "--format", "json")
    Assert-Condition ($build2.Json.status -eq "succeeded" -and $build2.Json.buildJobId -ne $build1.Json.buildJobId -and $build2.Json.buildContentId -eq $build1.Json.buildContentId) "Repeated build identity contract failed."
    Assert-Condition ((Get-FileHash (Join-Path $build1Path "build-result.json") -Algorithm SHA256).Hash -eq $build1Hash) "The first build changed after the repeat."
    $build2Path = Join-Path $acceptanceRoot "builds\$($build2.Json.buildJobId)"
    Assert-Condition ((Get-ChildItem $build1Path -Recurse -File).Count -gt 0 -and (Get-ChildItem $build2Path -Recurse -File).Count -gt 0) "Repeated build artifacts were not isolated."

    $text = Invoke-Native "dotnet" @("run", "--project", $cliProject, "--no-build", "--", "build", "--plan", $frozenPath, "--output", $acceptanceRoot, "--blender-path", $blenderArgument, "--format", "text")
    Assert-Condition ($text.ExitCode -eq 0 -and $text.Output.Contains("Status: Succeeded") -and $text.Output.Contains("ThreeDTiles=Succeeded")) "CLI text mode did not report success."

    $validationSource = @"
using System.Text.Json;
using SceneBuilder.Blender;
using SceneBuilder.Pipeline;
using SceneBuilder.Tiles;

if (args.Length != 2) return 2;
var packagePath = Path.GetFullPath(args[0]);
var tilesetPath = Path.GetFullPath(args[1]);
var glb = new BinaryGlbValidator();
var single = glb.Validate(Path.GetFullPath(Path.Combine(packagePath, "..", "single-glb", "scene.glb")));
var package = await new ScenePackageValidator(glb).ValidateAsync(packagePath, CancellationToken.None);
var tiles = await new TilesetValidator(new ScenePackageValidator(glb), glb).ValidateAsync(packagePath, tilesetPath, CancellationToken.None);
Console.WriteLine(JsonSerializer.Serialize(new { SingleGlbValid = single.IsValid, PackageValid = package.IsValid, TilesetValid = tiles.IsValid, UriSafe = package.Index?.Partitions.All(item => item.ArtifactPath is not null && !Path.IsPathRooted(item.ArtifactPath) && !item.ArtifactPath.Contains("..", StringComparison.Ordinal) && item.ArtifactPath.StartsWith("partitions/", StringComparison.Ordinal)) ?? false }));
return single.IsValid && package.IsValid && tiles.IsValid ? 0 : 1;
"@
    $validationProject = "<Project Sdk=`"Microsoft.NET.Sdk`">`n  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>`n  <ItemGroup>`n    <Compile Include=`"ValidationProgram.cs`" />`n    <ProjectReference Include=`"__REPO_ROOT__\src\SceneBuilder.Blender\SceneBuilder.Blender.csproj`" />`n    <ProjectReference Include=`"__REPO_ROOT__\src\SceneBuilder.Pipeline\SceneBuilder.Pipeline.csproj`" />`n    <ProjectReference Include=`"__REPO_ROOT__\src\SceneBuilder.Tiles\SceneBuilder.Tiles.csproj`" />`n  </ItemGroup>`n</Project>`n"
    $validationProject = $validationProject.Replace("__REPO_ROOT__", $repoRoot)
    Write-Utf8 $validationProgramPath $validationSource
    Write-Utf8 $validationHelperProject $validationProject
    $validationBuild = Invoke-Native "dotnet" @("build", $validationHelperProject, "--nologo")
    Assert-Condition ($validationBuild.ExitCode -eq 0) ("Validation helper build failed: {0}" -f $validationBuild.Output)
    $packagePath = Join-Path $build1Path "scene-package"
    $validator = Invoke-HelperJson $validationHelperProject @($packagePath, (Join-Path $packagePath "tileset.json"))
    Assert-Condition ($validator.Json.singleGlbValid -and $validator.Json.packageValid -and $validator.Json.tilesetValid -and $validator.Json.uriSafe) "Existing validators did not accept the real artifacts."

    $glbForImport = (Join-Path $build1Path "single-glb\scene.glb").Replace("\", "/")
    $import = Invoke-Native $BlenderPath @("--background", "--factory-startup", "--python-expr", "import bpy; bpy.ops.import_scene.gltf(filepath=r'$glbForImport'); print('SCENE_BUILDER_GLB_IMPORT_OK'); print('OBJECT_COUNT=' + str(len(bpy.context.scene.objects)))")
    $objectMatch = [regex]::Match($import.Output, "OBJECT_COUNT=(\d+)")
    Assert-Condition ($import.ExitCode -eq 0 -and $import.Output.Contains("SCENE_BUILDER_GLB_IMPORT_OK") -and $objectMatch.Success -and [int]$objectMatch.Groups[1].Value -gt 0) "Blender GLB re-import did not pass."

    $missingPath = Join-Path $acceptanceRoot "missing-blender.exe"
    $notConfigured = Invoke-CliJson @("build", "--plan", $frozenPath, "--output", $acceptanceRoot, "--blender-path", $missingPath, "--format", "json") 4
    $notConfiguredPath = Join-Path $acceptanceRoot "builds\$($notConfigured.Json.buildJobId)"
    Assert-Condition ($notConfigured.Json.status -eq "notConfigured" -and (Get-ChildItem $notConfiguredPath -Recurse -Filter "*.glb" -File -ErrorAction SilentlyContinue).Count -eq 0 -and -not (Test-Path (Join-Path $notConfiguredPath "scene-package"))) "Missing Blender did not produce the required NotConfigured result."

    $timeout = Invoke-CliJson @("build", "--plan", $frozenPath, "--output", $acceptanceRoot, "--blender-path", $blenderArgument, "--timeout-seconds", "0.001", "--format", "json") 5
    $timeoutPath = Join-Path $acceptanceRoot "builds\$($timeout.Json.buildJobId)"
    Assert-Condition ($timeout.Json.status -eq "failed" -and ($timeout.Json.diagnostics.code -contains "BLENDER_PROCESS_TIMED_OUT") -and (Get-ChildItem $timeoutPath -Recurse -Filter "*.glb" -File -ErrorAction SilentlyContinue).Count -eq 0 -and -not (Test-Path (Join-Path $timeoutPath "scene-package"))) "Timeout behavior did not stop downstream outputs."
    Assert-Condition ((Get-ChildItem (Join-Path $acceptanceRoot "builds") -Force -Directory | Where-Object Name -like ".staging-*" | Measure-Object).Count -eq 0) "Build staging directories were not cleaned up."

    $failure = $false
    [ordered]@{
        BlenderVersion = $versionLine
        FactoryStartupSmoke = "exit 0 + SCENE_BUILDER_BLENDER_OK"
        Fixture = "tests/fixtures/synthetic/public-synthetic-closed-polyline.dxf"
        Plan = "revision-0002; validate valid; freeze ready"
        FirstBuild = $build1.Json.buildJobId
        RepeatedBuild = $build2.Json.buildJobId
        SameBuildContentId = ($build1.Json.buildContentId -eq $build2.Json.buildContentId)
        FirstBuildUnchanged = $true
        ExistingValidators = "GLB + Scene Package + Tileset valid"
        BlenderReimport = "SCENE_BUILDER_GLB_IMPORT_OK; OBJECT_COUNT>0"
        NotConfigured = "exit 4; no GLB/package/tiles"
        Timeout = "exit 5; BLENDER_PROCESS_TIMED_OUT; no downstream outputs"
        TextMode = "exit 0; Status: Succeeded"
    } | ConvertTo-Json
} catch {
    Write-Error ("{0}`n{1}" -f $_, $_.InvocationInfo.PositionMessage)
    throw
} finally {
    if (-not $KeepOutputOnFailure -and (Test-Path -LiteralPath $helperRoot)) { Remove-Item -LiteralPath $helperRoot -Recurse -Force -ErrorAction SilentlyContinue }
    if ($failure -and -not $KeepOutputOnFailure -and -not $providedOutputRoot -and (Test-Path -LiteralPath $acceptanceRoot)) { Remove-Item -LiteralPath $acceptanceRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

exit 0
