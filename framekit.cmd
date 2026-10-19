@echo off
setlocal
set DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project "%~dp0tools\FrameKit" -c Release -v quiet -tl:off -- %*
