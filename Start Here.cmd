@echo off
setlocal
cd /d "%~dp0"
title Steam Frame eye tracking
set "PS=powershell -NoProfile -ExecutionPolicy Bypass -File"
set "PY="
where py >nul 2>nul && set "PY=py -3"
if not defined PY where python >nul 2>nul && set "PY=python"

:menu
cls
echo.
echo   Steam Frame eye tracking for VRChat (VRCFaceTracking)
echo   -----------------------------------------------------
tasklist /fi "imagename eq VRCFaceTracking.exe" 2>nul | find /i "VRCFaceTracking.exe" >nul
if errorlevel 1 (
  echo   ! VRCFaceTracking is not running. Press S to start it.
  echo.
)
echo    1  Set up / update on this PC
echo    2  Check status             (spoken result)
echo    3  Calibrate eyelids        (spoken prompts, about 70 s, headset on)
echo    4  Live eyelid view
echo    5  Open the settings folder
echo    6  Uninstall (puts the stock SteamLink module back)
echo    S  Start VRCFaceTracking
echo    A  Advanced: the headset part (SSH)
echo    Q  Quit
echo.
choice /c 123456SAQ /n /m "  Choose: "
set "C=%errorlevel%"
echo.
if "%C%"=="1" %PS% scripts\setup.ps1 & goto done
if "%C%"=="2" %PS% scripts\doctor.ps1 & goto done
if "%C%"=="3" goto calibrate
if "%C%"=="4" goto watch
if "%C%"=="5" goto folder
if "%C%"=="6" %PS% scripts\setup.ps1 -Uninstall & goto done
if "%C%"=="7" goto startvrcft
if "%C%"=="8" goto advanced
exit /b 0

:startvrcft
rem Through Steam: starting VRCFaceTracking.exe directly never loads the modules.
start "" "steam://rungameid/3329480"
echo   Starting VRCFaceTracking through Steam...
timeout /t 3 /nobreak >nul
goto menu

:advanced
cls
echo.
echo   Advanced: the headset part (frameeyeosc, over SSH)
echo   --------------------------------------------------
echo   Not needed with SteamOS 0.4.3+ and SteamVR 2.18+: SteamVR then sends each eye's eyelid itself.
echo   Only for older versions, or to try per-eye gaze. It needs Developer Mode and SSH on the headset.
echo.
echo    1  Set up / update the headset part
echo    2  Turn the headset part off (keeps it installed)
echo    B  Back
echo.
choice /c 12B /n /m "  Choose: "
set "C=%errorlevel%"
echo.
if "%C%"=="1" goto headset
if "%C%"=="2" goto headsetoff
goto menu

:askheadset
echo   The headset needs SSH enabled. Its address is on the headset under Settings, Network
echo   (or use the one you connect with in WinSCP / a terminal).
set "HS="
set /p "HS=  Headset login (for example steamos@192.168.1.50): "
exit /b 0

:headset
call :askheadset
if "%HS%"=="" goto advanced
%PS% scripts\setup.ps1 -Headset %HS%
goto done

:headsetoff
call :askheadset
if "%HS%"=="" goto advanced
%PS% scripts\setup.ps1 -Headset %HS% -HeadsetOff
goto done

:calibrate
if not defined PY (
  echo   Calibration needs Python 3 from https://www.python.org/downloads/ ^(tick "Add to PATH"^).
  goto done
)
%PY% tools\tune.py calibrate
goto done

:watch
if not defined PY (
  echo   The live view needs Python 3 from https://www.python.org/downloads/
  goto done
)
echo   Press Ctrl+C to stop.
%PY% tools\tune.py watch
goto done

:folder
for /d %%D in ("%APPDATA%\VRCFaceTracking\CustomLibs\*") do if exist "%%D\SteamFrameVRCFTModule.dll" (start "" "%%D" & goto done)
echo   The module is not installed yet. Choose 1 first.

:done
echo.
pause
goto menu
