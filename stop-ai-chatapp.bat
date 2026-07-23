@echo off
setlocal

echo Stopping AIChatApp processes...

powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Process AIChatApp.API,AIChatApp.Gateway,AIChatApp.Web,AIChatApp.MLTraining -ErrorAction SilentlyContinue | Stop-Process -Force"

echo Done. You can build the solution again.

endlocal
