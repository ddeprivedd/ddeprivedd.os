@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK was not found.
  echo Install the free .NET SDK from https://dotnet.microsoft.com/download and run this file again.
  pause
  exit /b 1
)

echo Building ddeprivedd.os...
dotnet build "ddeprivedd.os.csproj" -c Release -o "%~dp0dist"
if errorlevel 1 (
  echo.
  echo Build failed. Copy the error text above if you need help fixing it.
  pause
  exit /b 1
)

echo.
echo Done. Your exe is in the "dist" folder next to this file.
pause
