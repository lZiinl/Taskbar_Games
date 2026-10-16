# 포트리스 전투 종료 처리와 파편 수명만 격리해서 검사하는 PowerShell 테스트.
# 창 생성·레지스트리 접근 없이 필요한 필드만 직접 주입한다.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\..\bin\Taskbar_Games.exe')) | Out-Null
$gameType = [TaskbarGames.Games.Fortress.FortressGame]
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
# Exercise combat state without creating a window or loading user settings.
# 생성자를 호출하지 않은 객체이므로 필드 초기화 식도 실행되지 않는다.
# 호출할 메서드가 쓰는 컬렉션과 난수·좌표를 아래에서 반드시 준비한다.
$game = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($gameType)
# Reflection으로 내부 상태를 설정한다. 실제 사용자 설정이나 UI는 건드리지 않는다.
function Set-Field($name, $value) { $gameType.GetField($name, $flags).SetValue($game, $value) }
# 검증 대상 필드 값을 읽는다. 기대값과 다르면 아래 조건에서 예외를 발생시킨다.
function Read-Field($name) { $gameType.GetField($name, $flags).GetValue($game) }
$debrisField = $gameType.GetField('debris', $flags)
$debrisField.SetValue($game, [Activator]::CreateInstance($debrisField.FieldType))
# 고정 시드로 파편의 초기 속도와 수명을 재현한다.
Set-Field 'effectRandom' ([Random]::new(42))
Set-Field 'ground' ([single]600)
Set-Field 'targetX' ([single]400)
$finish = $gameType.GetMethod('Finish', $flags)
$update = $gameType.GetMethod('UpdateDebris', $flags)
# 첫 명중: 콤보 1, 명중 1, 파편 28개를 기대한다. 다음 명중으로 연속 콤보도 확인한다.
$finish.Invoke($game, @($true, [double]0)) | Out-Null
if ((Read-Field 'combo') -ne 1 -or (Read-Field 'hits') -ne 1) { throw 'First hit failed' }
if ($debrisField.GetValue($game).Count -ne 28) { throw 'Missing impact particles' }
$finish.Invoke($game, @($true, [double]2)) | Out-Null
if ((Read-Field 'combo') -ne 2) { throw 'Consecutive hit failed' }
# 60Hz의 1초를 실제 대기 없이 진행하여 파편이 수명/지면 조건으로 전부 제거되는지 확인한다.
for ($i = 0; $i -lt 60; $i++) { $update.Invoke($game, @([double](1.0 / 60))) | Out-Null }
if ($debrisField.GetValue($game).Count -ne 0) { throw 'Particles did not expire' }
# 실패는 누적 명중을 지우지 않고 콤보만 끊어야 한다. 이후 명중은 새 콤보 1로 시작한다.
$finish.Invoke($game, @($false, [double]4)) | Out-Null
if ((Read-Field 'combo') -ne 0 -or (Read-Field 'hits') -ne 2) { throw 'Miss reset failed' }
$finish.Invoke($game, @($true, [double]6)) | Out-Null
if ((Read-Field 'combo') -ne 1 -or (Read-Field 'hits') -ne 3) { throw 'New streak failed' }
Write-Output 'PASS: consecutive hits, miss reset, new streak, impact spawn and particle cleanup.'
