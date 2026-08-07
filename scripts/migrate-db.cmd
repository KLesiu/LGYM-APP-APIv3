@echo off
setlocal

if "%LGYM_MIGRATION_POSTGRES%"=="" (
  echo LGYM_MIGRATION_POSTGRES is required for offline schema bootstrap.
  exit /b 1
)

dotnet run --project "LgymApi.DataSeeder" -- --migrate-only
if errorlevel 1 exit /b %errorlevel%

dotnet run --project "LgymApi.DataSeeder" -- --prepare-hangfire
if errorlevel 1 exit /b %errorlevel%

endlocal
