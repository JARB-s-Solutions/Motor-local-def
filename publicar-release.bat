@echo off
setlocal
cd /d "%~dp0"
dotnet publish .\HexodusMotorLocal.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\release-win-x64
