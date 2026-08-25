@echo off
REM Build dev (wildlife + RawMeat) via Unity batchmode - tidak bergantung MCP session.
set UNITY="C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
set PROJECT=C:\Users\carlo\OneDrive\Documents\Github Project\Unity\ProjectMultiplayer
set OUT=%PROJECT%\Builds\DevWildlife

if exist "%OUT%" rmdir /s /q "%OUT%"

%UNITY% -batchmode -nographics -silent-crashes -projectPath "%PROJECT%" ^
  -buildWindows64Player "%OUT%\ProjectMultiplayer.exe" ^
  -logFile "%PROJECT%\Builds\build_dev.log" ^
  -quit

echo Build exit code: %ERRORLEVEL%
if exist "%OUT%\ProjectMultiplayer.exe" (
  echo BUILD SUCCESS
) else (
  echo BUILD FAILED - cek Builds\build_dev.log
)
pause
