@echo off
rem Starts PEAK through Steam with BepInEx from the PeakCraft mod-manager profile.
rem Needs winhttp.dll (Doorstop) in the PEAK folder; doorstop_config.ini there keeps plain launches vanilla.
set "PROFILE=%APPDATA%\Thunderstore Mod Manager\DataFolder\PEAK\profiles\PeakCraft"
if not "%~1"=="" set "PROFILE=%~1"
start "" "C:\Program Files (x86)\Steam\steam.exe" -applaunch 3527290 --doorstop-enabled true --doorstop-target-assembly "%PROFILE%\BepInEx\core\BepInEx.Preloader.dll"
