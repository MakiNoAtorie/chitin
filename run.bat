@echo off
rem Run from source for quick testing. Arguments pass through to the app.
cd /d "%~dp0"
dotnet run -- %* || pause
