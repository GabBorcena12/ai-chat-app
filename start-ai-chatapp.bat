@echo off
setlocal

set "ROOT=%~dp0"
set "ASPNETCORE_ENVIRONMENT=Development"

echo Starting AIChatApp projects in separate terminal windows...
echo.

echo Building solution first to avoid DLL copy locks...
dotnet build "%ROOT%AIChatApp.sln" -v:minimal
if errorlevel 1 (
    echo.
    echo Build failed. Stop any running AIChatApp windows, then try again.
    pause
    exit /b 1
)

echo Opening AIChatApp.API terminal...
start "AIChatApp.API" /D "%ROOT%" cmd /k "title AIChatApp.API && dotnet run --no-build --project AIChatApp.API\AIChatApp.API.csproj --launch-profile https"

echo Giving the API a few seconds to bind its ports...
timeout /t 5 /nobreak >nul

echo Opening AIChatApp.Gateway terminal...
start "AIChatApp.Gateway" /D "%ROOT%" cmd /k "title AIChatApp.Gateway && dotnet run --no-build --project AIChatApp.Gateway\AIChatApp.Gateway.csproj --launch-profile https"

echo Opening AIChatApp.Web terminal...
start "AIChatApp.Web" /D "%ROOT%" cmd /k "title AIChatApp.Web && dotnet run --no-build --project AIChatApp.Web\AIChatApp.Web.csproj --launch-profile https"

echo Opening AIChatApp.MLTraining terminal...
start "AIChatApp.MLTraining" /D "%ROOT%" cmd /k "title AIChatApp.MLTraining && dotnet run --no-build --project AIChatApp.MLTraining\AIChatApp.MLTraining.csproj --launch-profile AIChatApp.MLTraining"

echo Started:
echo   API       https://localhost:7093
echo   Gateway   https://localhost:7067
echo   Web       https://localhost:7033
echo   Training  https://localhost:55191
echo.
echo Close each console window to stop its project.

endlocal
