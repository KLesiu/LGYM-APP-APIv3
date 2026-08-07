param(
    [string]$MongoConnection = "",
    [string]$MongoDatabase = ""
)

if ($MongoConnection -or $MongoDatabase) {
    Write-Warning "The -MongoConnection and -MongoDatabase parameters are deprecated and ignored by scripts/setup-all.ps1."
}

& "${PSScriptRoot}\migrate-db.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
