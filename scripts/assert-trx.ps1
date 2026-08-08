param(
    [Parameter(Mandatory)]
    [string]$SuiteName,
    [Parameter(Mandatory)]
    [string]$RepositoryRoot,
    [Parameter(Mandatory)]
    [string]$ExpectedHead,
    [Parameter(Mandatory)]
    [string]$BuildManifestPath,
    [Parameter(Mandatory)]
    [string]$DiscoveryManifestPath,
    [Parameter(Mandatory)]
    [string]$TrxDirectory,
    [string]$ExpectedTrxFileName = "",
    [Parameter(Mandatory)]
    [string]$NotBeforeUtc,
    [Parameter(Mandatory)]
    [int]$CommandExitCode,
    [Parameter(Mandatory)]
    [string]$EvidenceDirectory,
    [Parameter(Mandatory)]
    [string]$CommandJson,
    [switch]$Supplementary,
    [string]$CoveragePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "VerificationEvidence.psm1") -Force

try {
    if ($ExpectedHead -notmatch '^[0-9a-f]{40}$') {
        throw "The expected SHA must be a full lowercase Git SHA."
    }

    $notBefore = [System.DateTimeOffset]::MinValue
    if (-not [System.DateTimeOffset]::TryParse($NotBeforeUtc, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$notBefore)) {
        throw "The test command start timestamp is malformed."
    }

    try {
        $parsedCommand = $CommandJson | ConvertFrom-Json -Depth 8
    }
    catch {
        throw "The test command array is malformed JSON."
    }

    if ($parsedCommand -is [string] -or $parsedCommand -isnot [System.Collections.IEnumerable]) {
        throw "The test command must be a JSON array."
    }

    $command = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in $parsedCommand) {
        if ($argument -isnot [string] -or [string]::IsNullOrWhiteSpace($argument)) {
            throw "The test command must contain only non-empty string arguments."
        }

        $command.Add($argument)
    }

    $commandArguments = $command.ToArray()
    if ($CommandExitCode -ne 0) {
        throw "The test command exited with code $CommandExitCode."
    }

    $snapshot = Get-RepositorySnapshot -RepositoryRoot $RepositoryRoot
    Assert-CleanRepositorySnapshot -Snapshot $snapshot
    if ($snapshot.head -cne $ExpectedHead) {
        throw "The active repository HEAD does not match the required SHA."
    }

    $build = Get-ReleaseBuildManifest -Path $BuildManifestPath -Snapshot $snapshot -ExpectedHead $ExpectedHead
    $discovery = Get-DiscoveryManifest -Path $DiscoveryManifestPath -SuiteName $SuiteName -Snapshot $snapshot -ExpectedHead $ExpectedHead -BuildCompletedUtc $build.completedUtc
    Assert-CommandMatchesDiscovery -Command $commandArguments -DeclaredFilters $discovery.filters -Supplementary:$Supplementary

    $trx = Get-ValidatedTrx -TrxDirectory $TrxDirectory -ExpectedTrxFileName $ExpectedTrxFileName -NotBeforeUtc $notBefore
    if (-not (Test-StringArraysEqualOrdinal -Left $trx.testNames -Right $discovery.tests)) {
        throw "The TRX test-name set does not match the same-SHA discovery manifest."
    }

    $coverage = $null
    if (-not [string]::IsNullOrWhiteSpace($CoveragePath)) {
        $coverage = Get-ValidatedOpenCoverReport -CoveragePath $CoveragePath -NotBeforeUtc $notBefore -RepositoryRoot $snapshot.path
    }

    $evidencePath = New-FreshEvidenceDirectory -Path $EvidenceDirectory
    $summaryPath = Join-Path $evidencePath "trx-summary.json"
    $summary = [pscustomobject]@{
        schemaVersion = 1
        kind = "trx-summary"
        suite = $SuiteName
        verificationScope = if ($Supplementary) { "Supplementary" } else { "Full" }
        completeSuite = -not $Supplementary
        generatedUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        repository = $snapshot
        buildManifest = [pscustomobject]@{
            path = (ConvertTo-AbsolutePath -Path $BuildManifestPath)
            sha256 = (Get-FileSha256 -Path $BuildManifestPath)
        }
        discoveryManifest = [pscustomobject]@{
            path = (ConvertTo-AbsolutePath -Path $DiscoveryManifestPath)
            sha256 = (Get-FileSha256 -Path $DiscoveryManifestPath)
            testListSha256 = $discovery.manifest.testListSha256
            identityScheme = $discovery.manifest.identityScheme
        }
        testIdentity = [pscustomobject]@{
            discovery = "vstest display-name multiset"
            execution = "trx executionId"
        }
        command = [pscustomobject]@{
            arguments = @($commandArguments)
            exitCode = $CommandExitCode
            notBeforeUtc = $notBefore.ToString("O")
        }
        trx = [pscustomobject]@{
            path = $trx.file.FullName
            fileName = $trx.file.Name
            sha256 = (Get-FileSha256 -Path $trx.file.FullName)
            bytes = $trx.file.Length
            lastWriteUtc = $trx.file.LastWriteTimeUtc.ToString("O")
            counters = $trx.counters
            testCount = $trx.testNames.Count
            testNames = @($trx.testNames)
        }
    }

    if ($null -ne $coverage) {
        $summary | Add-Member -NotePropertyName "coverage" -NotePropertyValue ([pscustomobject]@{
                path = $coverage.file.FullName
                fileName = $coverage.file.Name
                sha256 = (Get-FileSha256 -Path $coverage.file.FullName)
                bytes = $coverage.file.Length
                lastWriteUtc = $coverage.file.LastWriteTimeUtc.ToString("O")
                moduleCount = $coverage.moduleCount
                fileCount = $coverage.fileCount
                localPathMode = $coverage.localPathMode
            })
    }

    Write-RedactedJsonFile -Path $summaryPath -Value $summary -NoClobber
    Protect-EvidenceValue -Value $summary | ConvertTo-Json -Depth 32
}
catch {
    Write-Error (Protect-EvidenceString -Value $_.Exception.Message)
    exit 1
}
