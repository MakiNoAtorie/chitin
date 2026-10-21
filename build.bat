@echo off
rem Publish a portable single-file exe to out\win-x64\Chitin.exe.
cd /d "%~dp0"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o out\win-x64 || pause
