# 빌드 후 모든 회귀 테스트를 실행하는 단일 진입점.
# 게임 EXE를 참조하는 테스트 DLL을 만들고 각 테스트 클래스의 Run을 호출한다.

# 기본 검사는 창을 생성하지 않는다. 실제 창 검사는 명시적으로 -IncludeUI를 지정할 때만 실행한다.
param([switch]$IncludeUI)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
# 항상 최신 소스를 먼저 빌드한다. 실행 중인 게임이 EXE를 잠그고 있으면 빌드 단계에서 실패한다.
& (Join-Path $PSScriptRoot 'build.ps1')
# 별도 PowerShell 전투 테스트는 FortressSession의 공개 API로 충전·발사·명중·콤보를 검사한다.
& (Join-Path $projectRoot 'tests\Fortress\Combat.Tests.ps1')
$assembly = Join-Path $projectRoot 'bin\Taskbar_Games.exe'
$artifacts = Join-Path $projectRoot 'artifacts\tests'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$testAssembly = Join-Path $artifacts 'Taskbar_Games.Tests.dll'
# tests 하위의 C#을 전부 컴파일한다. 새 테스트 클래스의 실행 호출도 아래 목록에 추가해야 한다.
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
& $compiler /nologo /codepage:65001 /target:library /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll "/reference:$assembly" "/out:$testAssembly" @testSources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
# 동일 PowerShell 프로세스에 테스트 DLL을 로드한다. 결과물 PNG는 artifacts/tests에 저장한다.
[Reflection.Assembly]::LoadFrom($testAssembly) | Out-Null
# 각 Run은 실패 시 예외를 발생시킨다. 성공 로그만으로 건너뛴 검사를 숨기지 않고 첫 실패에서 중단한다.
if ($IncludeUI) {
    [FortressRegressionTests]::Run($artifacts)
    [LauncherRegressionTests]::Run($artifacts)
}
[BaseballRegressionTests]::Run($artifacts, $IncludeUI.IsPresent)
[PetRegressionTests]::Run($artifacts, $IncludeUI.IsPresent)
[RunnerRegressionTests]::Run($artifacts, $IncludeUI.IsPresent)
[SharedRegressionTests]::Run($artifacts, $IncludeUI.IsPresent)
[ChallengeRegressionTests]::Run($artifacts, $IncludeUI.IsPresent)
[CustomizationRegressionTests]::Run($artifacts, $IncludeUI.IsPresent)
[OnlineRegressionTests]::Run()
if (-not $IncludeUI) { Write-Host 'PASS: headless checks completed. UI checks skipped (opt in with -IncludeUI).' }
