# 배포용 ZIP과 SHA-256 목록을 만든다. 외부 업로드나 자동 설치는 하지 않는다.
param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1')
$versionSource = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\Shared\OnlineService.cs'))
$releaseVersion = [regex]::Match($versionSource, 'const string Version = "(\d+\.\d+\.\d+)"').Groups[1].Value
if (-not $releaseVersion) { throw 'Release version not found.' }
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'bin\Taskbar_Games.exe')) | Out-Null
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $projectRoot ('artifacts\releases\' + $releaseVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; existing releases are not overwritten.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$stage = Join-Path $OutputDirectory 'Taskbar_Games'
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Taskbar_Games.exe') -Destination $stage
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PLAYER-GUIDE.md') -Destination (Join-Path $stage '사용안내.md')
$zip = Join-Path $OutputDirectory 'Taskbar_Games.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'), ($hash + '  Taskbar_Games.zip' + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
$manifest = [ordered]@{
    version = $releaseVersion
    protocol = [TaskbarGames.Shared.ReleaseInfo]::Protocol
    rules = [ordered]@{
        Fortress = [TaskbarGames.Shared.ReleaseInfo]::Rule([TaskbarGames.Shared.GameKind]::Fortress)
        Baseball = [TaskbarGames.Shared.ReleaseInfo]::Rule([TaskbarGames.Shared.GameKind]::Baseball)
        Runner = [TaskbarGames.Shared.ReleaseInfo]::Rule([TaskbarGames.Shared.GameKind]::Runner)
    }
    file = 'Taskbar_Games.zip'
    sha256 = $hash
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'release.json'), ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Output "Package: $zip"
Write-Output "SHA256: $hash"
