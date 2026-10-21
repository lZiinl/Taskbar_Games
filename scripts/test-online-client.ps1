param([Parameter(Mandatory=$true)][string]$Endpoint)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'bin\Taskbar_Games.exe')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'artifacts\tests\Taskbar_Games.Tests.dll')) | Out-Null
[OnlineRegressionTests]::Integration($Endpoint)
