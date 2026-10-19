#!/bin/sh
export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec dotnet run --project "$(dirname "$0")/tools/FrameKit" -c Release -v quiet -tl:off -- "$@"
