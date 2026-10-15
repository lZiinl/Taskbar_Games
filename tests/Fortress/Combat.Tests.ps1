$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\..\bin\Taskbar_Games.exe')) | Out-Null
$gameType = [TaskbarGames.Games.Fortress.FortressGame]
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
# Exercise combat state without creating a window or loading user settings.
$game = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($gameType)
function Set-Field($name, $value) { $gameType.GetField($name, $flags).SetValue($game, $value) }
function Read-Field($name) { $gameType.GetField($name, $flags).GetValue($game) }
$debrisField = $gameType.GetField('debris', $flags)
$debrisField.SetValue($game, [Activator]::CreateInstance($debrisField.FieldType))
Set-Field 'effectRandom' ([Random]::new(42))
Set-Field 'ground' ([single]600)
Set-Field 'targetX' ([single]400)
$finish = $gameType.GetMethod('Finish', $flags)
$update = $gameType.GetMethod('UpdateDebris', $flags)
$finish.Invoke($game, @($true, [double]0)) | Out-Null
if ((Read-Field 'combo') -ne 1 -or (Read-Field 'hits') -ne 1) { throw 'First hit failed' }
if ($debrisField.GetValue($game).Count -ne 28) { throw 'Missing impact particles' }
$finish.Invoke($game, @($true, [double]2)) | Out-Null
if ((Read-Field 'combo') -ne 2) { throw 'Consecutive hit failed' }
for ($i = 0; $i -lt 60; $i++) { $update.Invoke($game, @([double](1.0 / 60))) | Out-Null }
if ($debrisField.GetValue($game).Count -ne 0) { throw 'Particles did not expire' }
$finish.Invoke($game, @($false, [double]4)) | Out-Null
if ((Read-Field 'combo') -ne 0 -or (Read-Field 'hits') -ne 2) { throw 'Miss reset failed' }
$finish.Invoke($game, @($true, [double]6)) | Out-Null
if ((Read-Field 'combo') -ne 1 -or (Read-Field 'hits') -ne 3) { throw 'New streak failed' }
Write-Output 'PASS: consecutive hits, miss reset, new streak, impact spawn and particle cleanup.'
