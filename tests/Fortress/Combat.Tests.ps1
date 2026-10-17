# 공개 모델 API로 전투를 검증한다. 사용자 설정이나 UI는 사용하지 않는다.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\..\bin\Taskbar_Games.exe')) | Out-Null
$session = [TaskbarGames.Games.Fortress.FortressSession]::new([Random]::new(42), 0, 0, 0)
$session.ResetLayout(1800, 600, [Drawing.PointF]::new(87, 571), 300)
function Fire-Shot([bool]$hit) {
    if ($session.Result.Length -gt 0) { $session.Advance(2, 300) | Out-Null }
    $session.BeginCharge(300) | Out-Null
    $chargeSeconds = if ($hit) { 2 * ($session.TargetX - 87 - 70) / (1800 - 87 - 70) } else { 0 }
    while ($chargeSeconds -gt 0.0000001) {
        $step = [Math]::Min(0.01, $chargeSeconds)
        $session.Advance($step, 300) | Out-Null
        $chargeSeconds -= $step
    }
    $session.ReleaseCharge() | Out-Null
    for ($i = 0; $i -lt 2000 -and $session.Flying; $i++) { $session.Advance(0.01, 300) | Out-Null }
    if ($session.WasHit -ne $hit -or $session.Flying) { throw 'Ballistic result differs from requested shot.' }
}
Fire-Shot $true
if ($session.Combo -ne 1 -or $session.Hits -ne 1) { throw 'First hit failed.' }
Fire-Shot $true
if ($session.Combo -ne 2 -or $session.BestCombo -ne 2) { throw 'Consecutive hit failed.' }
Fire-Shot $false
if ($session.Combo -ne 0 -or $session.Hits -ne 2) { throw 'Miss reset failed.' }
Fire-Shot $true
if ($session.Combo -ne 1 -or $session.Hits -ne 3 -or $session.Shots -ne 4 -or $session.BestCombo -ne 2) { throw 'New streak failed.' }
Write-Output 'PASS: pure FortressSession ballistics, consecutive hits, miss reset, best combo and new streak.'
