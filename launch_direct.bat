@echo off
taskkill /f /im RimWorldWin64.exe 2>nul
timeout /t 1 /nobreak >nul
cd /d "C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
set SteamAppId=294100
start "" "RimWorldWin64.exe" -savedatafolder=D:\RimWorldDevData -quicktest -developer
echo Launch triggered from game directory successfully!
