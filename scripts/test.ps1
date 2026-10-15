$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $projectRoot 'tests\Fortress\Combat.Tests.ps1')
$assembly = Join-Path $projectRoot 'bin\Taskbar_Games.exe'
$artifacts = Join-Path $projectRoot 'artifacts\tests'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$testAssembly = Join-Path $artifacts 'Taskbar_Games.Tests.dll'
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
& $compiler /nologo /target:library /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$assembly" "/out:$testAssembly" @testSources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
[Reflection.Assembly]::LoadFrom($testAssembly) | Out-Null
[FortressRegressionTests]::Run($artifacts)
[BaseballRegressionTests]::Run($artifacts)
[LauncherRegressionTests]::Run($artifacts)
