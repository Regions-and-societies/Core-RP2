@echo off
taskkill /f /im RimWorldWin64.exe 2>nul
timeout /t 2 /nobreak >nul
copy /y "d:\github\rimsynapse\Regions-and-Territories\Assemblies\RimSynapseRegionsAndTerritories.dll" "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\RimSynapseRegionsAndTerritories\Assemblies\"
timeout /t 1 /nobreak >nul
start "" "C:\Program Files (x86)\Steam\steam.exe" -applaunch 294100 -quicktest
echo Applied updated DLL and launched quicktest via Steam!
