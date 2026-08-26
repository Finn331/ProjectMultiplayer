@echo off
REM Wildlife playtest launcher: P1 = host (create room), P2 = join.
set EXE="C:\Users\carlo\OneDrive\Documents\Github Project\Unity\ProjectMultiplayer\Builds\DevWildlife\ProjectMultiplayer.exe"
set ROOM=ROOM-PLAYTEST

echo Starting P1 (host)...
start "" %EXE% -autoJoin %ROOM% -playerName P1 -host 1 -autoForest 1
timeout /t 5 /nobreak >nul
echo Starting P2 (join)...
start "" %EXE% -autoJoin %ROOM% -playerName P2 -autoForest 1
echo Both clients launched. Check Player.log for wildlife spawn.
