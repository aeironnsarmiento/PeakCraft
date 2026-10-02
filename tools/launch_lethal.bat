@echo off
rem Starts Lethal Company through Steam with BepInEx from the LethalCraft mod-manager profile.
rem Needs winhttp.dll (Doorstop) in the game folder; doorstop_config.ini there keeps plain launches vanilla.
set "PROFILE=%APPDATA%\Thunderstore Mod Manager\DataFolder\LethalCompany\profiles\LethalCraft Dev"
if not "%~1"=="" set "PROFILE=%~1"
start "" "C:\Program Files (x86)\Steam\steam.exe" -applaunch 1966720 --doorstop-enabled true --doorstop-target-assembly "%PROFILE%\BepInEx\core\BepInEx.Preloader.dll"
