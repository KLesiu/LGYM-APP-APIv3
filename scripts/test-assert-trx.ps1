Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "VerificationEvidence.psm1") -Force

function Write-FixtureJson {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [object]$Value
    )

    [System.IO.File]::WriteAllText(
        $Path,
        (($Value | ConvertTo-Json -Depth 16) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
}

function Initialize-FixtureRepository {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $repository = New-Item -ItemType Directory -Path (Join-Path $Root "repository") -Force
    [System.IO.File]::WriteAllText((Join-Path $repository.FullName "fixture.txt"), "fixture`n", [System.Text.UTF8Encoding]::new($false))
    $null = & git -C $repository.FullName init --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Could not initialize the temporary fixture repository."
    }

    $null = & git -C $repository.FullName add fixture.txt
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stage the fixture repository file."
    }

    $null = & git -C $repository.FullName -c user.name=fixture -c user.email=fixture@example.invalid commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) {
        throw "Could not commit the fixture repository file."
    }

    $head = (& git -C $repository.FullName rev-parse HEAD).Trim()
    $branch = (& git -C $repository.FullName branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$' -or [string]::IsNullOrWhiteSpace($branch)) {
        throw "The fixture repository state is invalid."
    }

    return [pscustomobject]@{
        path = $repository.FullName
        head = $head
        branch = $branch
    }
}

function New-ManifestPair {
    param(
        [Parameter(Mandatory)]
        [object]$Repository,
        [Parameter(Mandatory)]
        [string]$Directory,
        [Parameter(Mandatory)]
        [string[]]$Tests,
        [string[]]$Filters = @(),
        [string]$SuiteName = "Fixture"
    )

    $timestamps = [pscustomobject]@{
        buildStarted = [System.DateTimeOffset]::UtcNow.AddMinutes(-2).ToString("O")
        buildCompleted = [System.DateTimeOffset]::UtcNow.AddMinutes(-1).ToString("O")
        discoveryStarted = [System.DateTimeOffset]::UtcNow.AddSeconds(-30).ToString("O")
        discoveryCompleted = [System.DateTimeOffset]::UtcNow.AddSeconds(-20).ToString("O")
    }
    $repositoryManifest = [pscustomobject]@{
        path = $Repository.path
        branch = $Repository.branch
        head = $Repository.head
        worktree = [pscustomobject]@{
            isClean = $true
            status = @()
        }
    }
    $buildPath = Join-Path $Directory "release-build.json"
    $discoveryPath = Join-Path $Directory "discovery.json"
    $buildManifest = [pscustomobject]@{
        schemaVersion = 1
        kind = "release-build"
        repository = $repositoryManifest
        startedUtc = $timestamps.buildStarted
        completedUtc = $timestamps.buildCompleted
        configuration = "Release"
        commands = @([pscustomobject]@{
                arguments = @("dotnet", "build", "fixture.sln", "--configuration", "Release")
                exitCode = 0
            })
        exitCode = 0
    }
    $discoveryManifest = [pscustomobject]@{
        schemaVersion = 1
        kind = "test-discovery"
        suite = $SuiteName
        repository = $repositoryManifest
        buildHead = $Repository.head
        startedUtc = $timestamps.discoveryStarted
        completedUtc = $timestamps.discoveryCompleted
        commands = @([pscustomobject]@{
                arguments = @("dotnet", "test", "fixture.csproj", "--list-tests")
                exitCode = 0
            })
        exitCode = 0
        declaredFilters = @($Filters)
        identityScheme = "vstest-display-name-multiset-utf8-v1"
        tests = @($Tests)
        testCount = $Tests.Count
        testListSha256 = Get-StringListSha256 -Values $Tests
    }
    Write-FixtureJson -Path $buildPath -Value $buildManifest
    Write-FixtureJson -Path $discoveryPath -Value $discoveryManifest

    return [pscustomobject]@{
        buildPath = $buildPath
        discoveryPath = $discoveryPath
    }
}

function New-Case {
    param(
        [Parameter(Mandatory)]
        [object]$Fixture,
        [Parameter(Mandatory)]
        [string]$Name,
        [string[]]$Tests = @("Fixture.Pass"),
        [string[]]$Filters = @(),
        [string]$SuiteName = "Fixture"
    )

    $directory = New-Item -ItemType Directory -Path (Join-Path $Fixture.artifactRoot $Name) -Force
    $manifests = New-ManifestPair -Repository $Fixture.repository -Directory $directory.FullName -Tests $Tests -Filters $Filters -SuiteName $SuiteName
    $trxDirectory = New-Item -ItemType Directory -Path (Join-Path $directory.FullName "trx") -Force
    return [pscustomobject]@{
        name = $Name
        suiteName = $SuiteName
        buildPath = $manifests.buildPath
        discoveryPath = $manifests.discoveryPath
        trxDirectory = $trxDirectory.FullName
        expectedTrxFileName = "$Name.trx"
        evidenceDirectory = Join-Path $directory.FullName "summary"
        notBeforeUtc = [System.DateTimeOffset]::UtcNow.AddSeconds(-2)
        command = @("dotnet", "test", "fixture.csproj")
    }
}

function Write-TrxFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$TestNames,
        [int]$Total,
        [int]$Executed,
        [int]$Passed,
        [int]$Failed,
        [int]$NotExecuted,
        [string]$Outcome = "Passed",
        [string[]]$ExecutionIds = @()
    )

    if ($ExecutionIds.Count -eq 0) {
        $ExecutionIds = @($TestNames | ForEach-Object { [Guid]::NewGuid().ToString() })
    }

    if ($ExecutionIds.Count -ne $TestNames.Count) {
        throw "The TRX fixture execution identity count must equal its test-name count."
    }

    $results = [System.Text.StringBuilder]::new()
    for ($index = 0; $index -lt $TestNames.Count; $index++) {
        [void]$results.AppendLine("    <UnitTestResult testName=`"$($TestNames[$index])`" executionId=`"$($ExecutionIds[$index])`" outcome=`"$Outcome`" />")
    }

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
$results  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="$Total" executed="$Executed" passed="$Passed" failed="$Failed" notExecuted="$NotExecuted" />
  </ResultSummary>
</TestRun>
"@
    [System.IO.File]::WriteAllText($Path, $xml, [System.Text.UTF8Encoding]::new($false))
}

function Invoke-AssertFixture {
    param(
        [Parameter(Mandatory)]
        [object]$Fixture,
        [Parameter(Mandatory)]
        [object]$Case,
        [int]$CommandExitCode = 0,
        [string[]]$Command = @(),
        [switch]$Supplementary,
        [switch]$OmitExpectedTrxFileName
    )

    if ($Command.Count -eq 0) {
        $Command = $Case.command
    }

    $arguments = @(
        "-NoProfile",
        "-File", $Fixture.assertScript,
        "-SuiteName", $Case.suiteName,
        "-RepositoryRoot", $Fixture.repository.path,
        "-ExpectedHead", $Fixture.repository.head,
        "-BuildManifestPath", $Case.buildPath,
        "-DiscoveryManifestPath", $Case.discoveryPath,
        "-TrxDirectory", $Case.trxDirectory,
        "-NotBeforeUtc", $Case.notBeforeUtc.ToString("O"),
        "-CommandExitCode", [string]$CommandExitCode,
        "-EvidenceDirectory", $Case.evidenceDirectory,
        "-CommandJson", ($Command | ConvertTo-Json -Compress)
    )
    if (-not $OmitExpectedTrxFileName) {
        $arguments += @("-ExpectedTrxFileName", $Case.expectedTrxFileName)
    }

    if ($Supplementary) {
        $arguments += "-Supplementary"
    }

    $originalErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& pwsh @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $originalErrorActionPreference
    }

    return [pscustomobject]@{
        exitCode = $exitCode
        output = (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine)
    }
}

function Assert-NonZeroFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [object]$Result
    )

    if ($Result.exitCode -eq 0) {
        throw "Fixture '$Name' unexpectedly succeeded."
    }
}

function Assert-ZeroFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [object]$Result
    )

    if ($Result.exitCode -ne 0) {
        throw "Fixture '$Name' unexpectedly failed: $($Result.output)"
    }
}

function Assert-StringArrayEquals {
    param(
        [Parameter(Mandatory)]
        [string[]]$Expected,
        [Parameter(Mandatory)]
        [string[]]$Actual,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-StringArraysEqualOrdinal -Left $Expected -Right $Actual)) {
        throw "$Description did not match the expected ordered test set."
    }
}

function Assert-MatrixConfigurationContract {
    param(
        [Parameter(Mandatory)]
        [string]$MatrixScript
    )

    $releaseOutput = @(& pwsh -NoProfile -File $MatrixScript -Configuration Release 2>&1)
    $releaseExitCode = $LASTEXITCODE
    $releaseText = ($releaseOutput | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($releaseExitCode -eq 0 -or $releaseText -notmatch 'Same-SHA evidence requires a clean worktree' -or $releaseText -match 'parameter cannot be found|Cannot find a parameter') {
        throw "The documented -Configuration Release invocation did not bind before the expected clean-worktree gate."
    }

    $unsupportedOutput = @(& pwsh -NoProfile -File $MatrixScript -Configuration Debug 2>&1)
    $unsupportedExitCode = $LASTEXITCODE
    $unsupportedText = ($unsupportedOutput | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($unsupportedExitCode -eq 0 -or $unsupportedText -notmatch 'Only the Release configuration is supported') {
        throw "The matrix runner did not clearly reject an unsupported configuration."
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lgym-assert-trx-" + [Guid]::NewGuid().ToString("N"))
try {
    $null = New-Item -ItemType Directory -Path $temporaryRoot -Force
    $repository = Initialize-FixtureRepository -Root $temporaryRoot
    $fixture = [pscustomobject]@{
        repository = $repository
        artifactRoot = (New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "artifacts") -Force).FullName
        assertScript = Join-Path $PSScriptRoot "assert-trx.ps1"
    }

    $polishVstestListing = @(
        "Ostrzeżenie: diagnostyka kompilacji nie jest listą testów.",
        "Przebieg testu dla: fixture.dll (.NETCoreApp,Version=v10.0)",
        "Wersja 18.0.1 (x64) VSTest",
        "    warning CS0000: an indented diagnostic before the list is not a test name.",
        "",
        "Dostępne są następujące testy:",
        "    Zeta.FixtureTest",
        "    Alpha.FixtureTest",
        "    Parametr.FixtureTest(`"x y`")",
        "",
        "Wynik testu: powodzenie.",
        "warning CS0000: diagnostics after the listing are not test names."
    )
    $listedPolishTests = Get-ListedTestNames -Lines $polishVstestListing
    Assert-StringArrayEquals -Expected @("Alpha.FixtureTest", "Parametr.FixtureTest(`"x y`")", "Zeta.FixtureTest") -Actual $listedPolishTests -Description "Polish VSTest listing"
    if ($listedPolishTests -contains "Ostrzeżenie: diagnostyka kompilacji nie jest listą testów." -or $listedPolishTests -contains "warning CS0000: an indented diagnostic before the list is not a test name." -or $listedPolishTests -contains "Wynik testu: powodzenie." -or $listedPolishTests -contains "warning CS0000: diagnostics after the listing are not test names.") {
        throw "The localized VSTest parser treated a diagnostic or localized message as a test name."
    }

    $mojibakeVstestListing = @(
        "Dostępne są następujące testy:",
        "    Parametr(`"Podci─ůganie: im mniejszy ci─Ö┼╝ar, tym lepiej`")"
    )
    $mojibakeDisplayTests = Get-ListedTestNames -Lines $mojibakeVstestListing
    Assert-StringArrayEquals -Expected @("Parametr(`"Podciąganie: im mniejszy ciężar, tym lepiej`")") -Actual $mojibakeDisplayTests -Description "OEM-852-mojibake VSTest listing"

    $collidingDisplayListing = @(
        "Dostępne są następujące testy:",
        "    SharedDisplayName",
        "    SharedDisplayName",
        "    DistinctDisplayName"
    )
    $collidingDisplayTests = Get-ListedTestNames -Lines $collidingDisplayListing
    Assert-StringArrayEquals -Expected @("DistinctDisplayName", "SharedDisplayName", "SharedDisplayName") -Actual $collidingDisplayTests -Description "Colliding VSTest display listing"

    Assert-MatrixConfigurationContract -MatrixScript (Join-Path $PSScriptRoot "run-verification-matrix.ps1")

    $collidingDisplay = New-Case -Fixture $fixture -Name "colliding-display" -Tests @("Fixture.Shared", "Fixture.Shared")
    Write-TrxFixture -Path (Join-Path $collidingDisplay.trxDirectory $collidingDisplay.expectedTrxFileName) -TestNames @("Fixture.Shared", "Fixture.Shared") -Total 2 -Executed 2 -Passed 2 -Failed 0 -NotExecuted 0
    Assert-ZeroFixture -Name "valid colliding display names" -Result (Invoke-AssertFixture -Fixture $fixture -Case $collidingDisplay)

    $valid = New-Case -Fixture $fixture -Name "valid"
    Write-TrxFixture -Path (Join-Path $valid.trxDirectory $valid.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $validResult = Invoke-AssertFixture -Fixture $fixture -Case $valid -Command @("dotnet", "test", "fixture.csproj", "-ConnectionString", "Host=localhost;Username=fixture;Password=fixture-password;Token=fixture-token")
    Assert-ZeroFixture -Name "valid full suite" -Result $validResult
    if ($validResult.output.Contains("fixture-password") -or $validResult.output.Contains("fixture-token") -or -not $validResult.output.Contains("[redacted]")) {
        throw "The valid evidence output did not redact credential-shaped values."
    }

    $validJson = $validResult.output | ConvertFrom-Json -Depth 32
    if ($validJson.kind -cne "trx-summary" -or -not $validJson.completeSuite -or $validJson.trx.testCount -ne 1) {
        throw "The valid evidence output did not have the expected JSON summary schema."
    }

    $summaryJson = [System.IO.File]::ReadAllText((Join-Path $valid.evidenceDirectory "trx-summary.json")) | ConvertFrom-Json -Depth 32
    if ($summaryJson.repository.head -cne $repository.head -or $summaryJson.command.exitCode -ne 0) {
        throw "The saved valid evidence summary is not valid JSON with the expected same-SHA fields."
    }

    $targeted = New-Case -Fixture $fixture -Name "targeted" -Tests @("Fixture.Target") -Filters @("FullyQualifiedName~Fixture.Target")
    Write-TrxFixture -Path (Join-Path $targeted.trxDirectory $targeted.expectedTrxFileName) -TestNames @("Fixture.Target") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $targetedResult = Invoke-AssertFixture -Fixture $fixture -Case $targeted -Supplementary -Command @("dotnet", "test", "fixture.csproj", "--filter", "FullyQualifiedName~Fixture.Target")
    Assert-ZeroFixture -Name "valid targeted supplementary suite" -Result $targetedResult
    $targetedJson = $targetedResult.output | ConvertFrom-Json -Depth 32
    if ($targetedJson.verificationScope -cne "Supplementary" -or $targetedJson.completeSuite) {
        throw "A filtered targeted fixture was allowed to claim full-suite success."
    }

    $missing = New-Case -Fixture $fixture -Name "missing"
    Assert-NonZeroFixture -Name "missing TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $missing)

    $stale = New-Case -Fixture $fixture -Name "stale"
    $stalePath = Join-Path $stale.trxDirectory $stale.expectedTrxFileName
    Write-TrxFixture -Path $stalePath -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    (Get-Item -LiteralPath $stalePath).LastWriteTimeUtc = $stale.notBeforeUtc.UtcDateTime.AddSeconds(-1)
    Assert-NonZeroFixture -Name "stale TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $stale)

    $zeroByte = New-Case -Fixture $fixture -Name "zero-byte"
    [System.IO.File]::WriteAllBytes((Join-Path $zeroByte.trxDirectory $zeroByte.expectedTrxFileName), [byte[]]@())
    Assert-NonZeroFixture -Name "zero-byte TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $zeroByte)

    $malformedTrx = New-Case -Fixture $fixture -Name "malformed-trx"
    [System.IO.File]::WriteAllText((Join-Path $malformedTrx.trxDirectory $malformedTrx.expectedTrxFileName), "<TestRun", [System.Text.UTF8Encoding]::new($false))
    Assert-NonZeroFixture -Name "malformed TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $malformedTrx)

    $malformedManifest = New-Case -Fixture $fixture -Name "malformed-manifest"
    Write-TrxFixture -Path (Join-Path $malformedManifest.trxDirectory $malformedManifest.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    [System.IO.File]::WriteAllText($malformedManifest.discoveryPath, "{", [System.Text.UTF8Encoding]::new($false))
    Assert-NonZeroFixture -Name "malformed discovery manifest" -Result (Invoke-AssertFixture -Fixture $fixture -Case $malformedManifest)

    $zeroTotal = New-Case -Fixture $fixture -Name "zero-total"
    Write-TrxFixture -Path (Join-Path $zeroTotal.trxDirectory $zeroTotal.expectedTrxFileName) -TestNames @() -Total 0 -Executed 0 -Passed 0 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "zero-total TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $zeroTotal)

    $failed = New-Case -Fixture $fixture -Name "failed"
    Write-TrxFixture -Path (Join-Path $failed.trxDirectory $failed.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 0 -Failed 1 -NotExecuted 0 -Outcome "Failed"
    Assert-NonZeroFixture -Name "failed TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $failed)

    $notExecuted = New-Case -Fixture $fixture -Name "not-executed"
    Write-TrxFixture -Path (Join-Path $notExecuted.trxDirectory $notExecuted.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 0 -Passed 0 -Failed 0 -NotExecuted 1 -Outcome "NotExecuted"
    Assert-NonZeroFixture -Name "not-executed TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $notExecuted)

    $multiple = New-Case -Fixture $fixture -Name "multiple"
    Write-TrxFixture -Path (Join-Path $multiple.trxDirectory $multiple.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Write-TrxFixture -Path (Join-Path $multiple.trxDirectory "other.trx") -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "multiple TRX files" -Result (Invoke-AssertFixture -Fixture $fixture -Case $multiple)

    $duplicateExecution = New-Case -Fixture $fixture -Name "duplicate-execution" -Tests @("Fixture.First", "Fixture.Second")
    $sharedExecutionId = [Guid]::NewGuid().ToString()
    Write-TrxFixture -Path (Join-Path $duplicateExecution.trxDirectory $duplicateExecution.expectedTrxFileName) -TestNames @("Fixture.First", "Fixture.Second") -Total 2 -Executed 2 -Passed 2 -Failed 0 -NotExecuted 0 -ExecutionIds @($sharedExecutionId, $sharedExecutionId)
    Assert-NonZeroFixture -Name "duplicate TRX execution identity" -Result (Invoke-AssertFixture -Fixture $fixture -Case $duplicateExecution)

    $wrongName = New-Case -Fixture $fixture -Name "wrong-name"
    Write-TrxFixture -Path (Join-Path $wrongName.trxDirectory "unexpected.trx") -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "wrong TRX file name" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongName)

    $wrongNameSet = New-Case -Fixture $fixture -Name "wrong-name-set"
    Write-TrxFixture -Path (Join-Path $wrongNameSet.trxDirectory $wrongNameSet.expectedTrxFileName) -TestNames @("Fixture.Other") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "wrong test-name set" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongNameSet)

    $wrongSha = New-Case -Fixture $fixture -Name "wrong-sha"
    Write-TrxFixture -Path (Join-Path $wrongSha.trxDirectory $wrongSha.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $wrongShaManifest = [System.IO.File]::ReadAllText($wrongSha.discoveryPath) | ConvertFrom-Json -Depth 32
    $wrongShaManifest.repository.head = "0000000000000000000000000000000000000000"
    Write-FixtureJson -Path $wrongSha.discoveryPath -Value $wrongShaManifest
    Assert-NonZeroFixture -Name "wrong manifest SHA" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongSha)

    $nonzero = New-Case -Fixture $fixture -Name "nonzero-command"
    Write-TrxFixture -Path (Join-Path $nonzero.trxDirectory $nonzero.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "nonzero test command" -Result (Invoke-AssertFixture -Fixture $fixture -Case $nonzero -CommandExitCode 73)

    $undeclaredFilter = New-Case -Fixture $fixture -Name "undeclared-filter"
    Write-TrxFixture -Path (Join-Path $undeclaredFilter.trxDirectory $undeclaredFilter.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "undeclared full-suite filter" -Result (Invoke-AssertFixture -Fixture $fixture -Case $undeclaredFilter -Command @("dotnet", "test", "fixture.csproj", "--filter", "FullyQualifiedName~Fixture.Pass"))

    $dirty = New-Case -Fixture $fixture -Name "dirty-worktree"
    Write-TrxFixture -Path (Join-Path $dirty.trxDirectory $dirty.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $dirtyPath = Join-Path $repository.path "dirty.txt"
    [System.IO.File]::WriteAllText($dirtyPath, "dirty`n", [System.Text.UTF8Encoding]::new($false))
    try {
        Assert-NonZeroFixture -Name "dirty worktree" -Result (Invoke-AssertFixture -Fixture $fixture -Case $dirty)
    }
    finally {
        Remove-Item -LiteralPath $dirtyPath -Force
    }

    Write-Host "assert-trx fixture matrix passed: full=1, colliding-display=1, targeted=1, failure=16, localized-listing=1, configuration=1."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
