:; exec bash "$(dirname "$0")/build.sh" "$@"
@powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
