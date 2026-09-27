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
echo    1  Set up / update on this PC
echo    2  Set up the headset too (over SSH)
echo    3  Check status             (spoken result)
echo    4  Calibrate eyelids        (spoken prompts, about 90 s, headset on)
echo    5  Live eyelid view
echo    6  Open the settings folder
echo    7  Uninstall (puts the stock SteamLink module back)
echo    Q  Quit
echo.
choice /c 1234567Q /n /m "  Choose: "
set "C=%errorlevel%"
echo.
if "%C%"=="1" %PS% scripts\setup.ps1 & goto done
if "%C%"=="2" goto headset
if "%C%"=="3" %PS% scripts\doctor.ps1 & goto done
if "%C%"=="4" goto calibrate
if "%C%"=="5" goto watch
if "%C%"=="6" goto folder
if "%C%"=="7" %PS% scripts\setup.ps1 -Uninstall & goto done
exit /b 0

:headset
echo   The headset needs SSH enabled. Its address is on the headset under Settings, Network
echo   (or use the one you connect with in WinSCP / a terminal).
set "HS="
set /p "HS=  Headset login (for example steamos@192.168.1.50): "
if "%HS%"=="" goto menu
%PS% scripts\setup.ps1 -Headset %HS%
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
