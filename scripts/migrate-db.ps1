if ([string]::IsNullOrWhiteSpace($env:LGYM_MIGRATION_POSTGRES)) {
    throw "LGYM_MIGRATION_POSTGRES is required for offline schema bootstrap."
}

dotnet run --project "LgymApi.DataSeeder" -- --migrate-only
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet run --project "LgymApi.DataSeeder" -- --prepare-hangfire
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
