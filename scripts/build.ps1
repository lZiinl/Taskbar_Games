# Windows 기본 .NET Framework 컴파일러로 모든 C# 소스를 하나의 WinExe로 빌드한다.
# 외부 패키지 복원은 없으며 아이콘은 Win32 실행 파일 리소스와 .NET 매니페스트 리소스 양쪽에 포함한다.

# PowerShell 작업 오류를 즉시 중단한다. 외부 컴파일러 오류는 아래에서 종료 코드로 별도 확인한다.
$ErrorActionPreference = 'Stop'
# 실행한 현재 폴더가 달라도 스크립트 위치를 기준으로 저장소 루트를 계산한다.
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
# 64비트 컴파일러가 없는 환경에서는 32비트 Framework 경로로 대체한다.
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$output = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $output -Force | Out-Null
# src 하위 C# 파일을 정렬해 입력 순서를 안정화한다. 새 게임 파일은 별도 프로젝트 파일 수정 없이 포함된다.
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
# /win32icon은 탐색기 아이콘, /resource는 각 Form에서 사용할 ArcadeTheme.AppIcon의 원본이다.
$iconPath = Join-Path $projectRoot 'assets\TaskbarGames.ico'
& $compiler /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32icon:$iconPath" "/resource:$iconPath,TaskbarGames.ico" "/out:$output\Taskbar_Games.exe" @sources
# 네이티브 컴파일러 실패는 PowerShell 예외가 아닐 수 있으므로 명시적으로 실패를 전달한다.
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $output\Taskbar_Games.exe"
