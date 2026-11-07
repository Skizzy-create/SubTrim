@echo off
rem Build both plugins and assemble ..\GameData\SubTrim (the folder to copy into KSP\GameData).
setlocal
cd /d "%~dp0"
dotnet build SubTrim.BDArmory\SubTrim.BDArmory.csproj -c Release -nologo || exit /b 1
set "OUT=%~dp0..\GameData\SubTrim"
if not exist "%OUT%\Plugins" mkdir "%OUT%\Plugins"
xcopy /e /y /q Distribution\GameData\SubTrim "%OUT%\" >nul || exit /b 1
copy /y SubTrim\bin\Release\net48\SubTrim.dll "%OUT%\Plugins\" >nul || exit /b 1
copy /y SubTrim.BDArmory\bin\Release\net48\SubTrim.BDArmory.dll "%OUT%\Plugins\" >nul || exit /b 1
echo Packaged to %OUT%
