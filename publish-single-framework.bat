@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "OUTPUT_DIR=%PROJECT_DIR%publish-single-framework"

dotnet publish "%PROJECT_DIR%FikaHeadlessManager.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%OUTPUT_DIR%"

if errorlevel 1 (
  echo.
  echo Publish failed.
  exit /b 1
)

echo.
echo Published framework-dependent single-file app to:

endlocal
