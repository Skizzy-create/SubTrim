@echo off
rem Build both plugins from Source\ and copy the DLLs into GameData\SubTrim\Plugins (the folder that ships).
rem Other KSP install:  package.cmd "C:\KSP"      (needs Source\refs\BDArmory.dll, see README "Building")
setlocal
cd /d "%~dp0"
set "KSPARG="
if not "%~1"=="" set "KSPARG=-p:KSPRoot=%~1"
dotnet build Source\SubTrim.BDArmory\SubTrim.BDArmory.csproj -c Release -nologo %KSPARG% || exit /b 1
dotnet run -c Release --project Source\Tests\ControllerSim\ControllerSim.csproj || exit /b 1
set "OUT=%~dp0GameData\SubTrim\Plugins"
if not exist "%OUT%" mkdir "%OUT%"
copy /y Source\SubTrim\bin\Release\net48\SubTrim.dll "%OUT%\" >nul || exit /b 1
copy /y Source\SubTrim.BDArmory\bin\Release\net48\SubTrim.BDArmory.dll "%OUT%\" >nul || exit /b 1
echo Packaged to %OUT%
