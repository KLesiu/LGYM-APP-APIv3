param(
    [Parameter(Mandatory)]
    [string]$EvidenceRoot,
    [Parameter(Mandatory)]
    [string]$Repository,
    [Parameter(Mandatory)]
    [string]$RunId,
    [Parameter(Mandatory)]
    [string]$RunAttempt,
    [Parameter(Mandatory)]
    [string]$Event,
    [Parameter(Mandatory)]
    [string]$Ref,
    [Parameter(Mandatory)]
    [string]$MergeSha,
    [Parameter(Mandatory)]
    [AllowEmptyString()]
    [string]$PullRequestHeadSha,
    [string]$SonarInputsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'VerificationEvidence.psm1') -Force

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $property = @($Object.PSObject.Properties | Where-Object { $_.Name -ceq $Name })
    if ($property.Count -ne 1 -or $null -eq $property[0].Value) {
        throw "Evidence is missing required property '$Name'."
    }

    return $property[0].Value
}

function Assert-ExpectedMergeSha {
    param(
        [Parameter(Mandatory)]
        [object]$Document,
        [Parameter(Mandatory)]
        [string]$Description
    )

    $repository = Get-RequiredProperty -Object $Document -Name 'repository'
    if ((Get-RequiredProperty -Object $repository -Name 'head') -cne $MergeSha) {
        throw "$Description does not match the required SHA."
    }

    $worktree = Get-RequiredProperty -Object $repository -Name 'worktree'
    if ((Get-RequiredProperty -Object $worktree -Name 'isClean') -isnot [bool] -or -not $worktree.isClean) {
        throw "$Description does not record a clean worktree."
    }
}

function Get-RequiredStringArray {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name
    if ($value -is [string]) {
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "Evidence property '$Name' must contain only non-empty strings."
        }
        return ,@($value)
    }

    if ($value -isnot [System.Collections.IEnumerable]) {
        throw "Evidence property '$Name' must be an array."
    }

    $values = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $value) {
        if ($item -isnot [string] -or [string]::IsNullOrWhiteSpace($item)) {
            throw "Evidence property '$Name' must contain only non-empty strings."
        }
        $values.Add($item)
    }

    return ,$values.ToArray()
}

function Get-RequiredNonNegativeInt {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name
    $parsed = 0
    if (-not [int]::TryParse([string]$value, [ref]$parsed) -or $parsed -lt 0) {
        throw "Evidence property '$Name' must be a non-negative integer."
    }

    return $parsed
}

function Get-ValidatedUploadedTrx {
    param([Parameter(Mandatory)][System.IO.FileInfo]$TrxFile)

    if ($TrxFile.Length -eq 0) {
        throw "Uploaded TRX '$($TrxFile.Name)' is empty."
    }

    try {
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $stringReader = [System.IO.StringReader]::new([System.IO.File]::ReadAllText($TrxFile.FullName))
        $reader = [System.Xml.XmlReader]::Create($stringReader, $settings)
        try {
            $document = [System.Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
        }
        finally {
            $reader.Dispose()
            $stringReader.Dispose()
        }
    }
    catch {
        throw "Uploaded TRX '$($TrxFile.Name)' is malformed XML."
    }

    if ($null -eq $document.DocumentElement -or $document.DocumentElement.LocalName -ne 'TestRun' -or [string]::IsNullOrWhiteSpace($document.DocumentElement.NamespaceURI)) {
        throw "Uploaded TRX '$($TrxFile.Name)' does not contain a valid TestRun document."
    }

    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaceManager.AddNamespace('trx', $document.DocumentElement.NamespaceURI)
    $counterNode = $document.SelectSingleNode('/trx:TestRun/trx:ResultSummary/trx:Counters', $namespaceManager)
    if ($counterNode -isnot [System.Xml.XmlElement]) {
        throw "Uploaded TRX '$($TrxFile.Name)' does not contain result counters."
    }

    $counters = [ordered]@{}
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'notExecuted')) {
        $value = $counterNode.GetAttribute($name)
        $parsed = 0
        if ([string]::IsNullOrWhiteSpace($value) -or -not [int]::TryParse($value, [ref]$parsed) -or $parsed -lt 0) {
            throw "Uploaded TRX '$($TrxFile.Name)' does not contain a valid '$name' counter."
        }
        $counters[$name] = $parsed
    }

    if ($counters.total -eq 0 -or $counters.executed -ne $counters.total -or $counters.passed -ne $counters.total -or $counters.failed -ne 0 -or $counters.notExecuted -ne 0) {
        throw "Uploaded TRX '$($TrxFile.Name)' does not have a non-empty, fully passing result."
    }

    $resultNodes = $document.SelectNodes('/trx:TestRun/trx:Results/trx:UnitTestResult', $namespaceManager)
    if ($null -eq $resultNodes -or $resultNodes.Count -ne $counters.total) {
        throw "Uploaded TRX '$($TrxFile.Name)' result count does not match its total counter."
    }

    $testNames = [System.Collections.Generic.List[string]]::new()
    $executionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($node in $resultNodes) {
        if ($node -isnot [System.Xml.XmlElement]) {
            throw "Uploaded TRX '$($TrxFile.Name)' contains an invalid result node."
        }

        $testName = $node.GetAttribute('testName')
        if ([string]::IsNullOrWhiteSpace($testName) -or $node.GetAttribute('outcome') -cne 'Passed') {
            throw "Uploaded TRX '$($TrxFile.Name)' contains a failed, unexecuted, or unnamed test result."
        }

        $executionId = $node.GetAttribute('executionId')
        $parsedExecutionId = [Guid]::Empty
        if ([string]::IsNullOrWhiteSpace($executionId) -or -not [Guid]::TryParse($executionId, [ref]$parsedExecutionId) -or -not $executionIds.Add($executionId)) {
            throw "Uploaded TRX '$($TrxFile.Name)' contains an invalid or duplicate execution identity."
        }

        $testNames.Add($testName)
    }

    if (@($document.SelectNodes("/trx:TestRun/trx:Results/trx:UnitTestResult[@outcome='NotExecuted']", $namespaceManager)).Count -ne 0) {
        throw "Uploaded TRX '$($TrxFile.Name)' contains NotExecuted result nodes."
    }

    return [pscustomobject]@{
        counters = [pscustomobject]$counters
        testNames = Get-SortedOrdinalStrings -Values $testNames.ToArray()
    }
}

function Get-RequiredNonNegativeLong {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name
    $parsed = [long]0
    if (-not [long]::TryParse([string]$value, [ref]$parsed) -or $parsed -lt 0) {
        throw "Evidence property '$Name' must be a non-negative integer."
    }

    return $parsed
}

function Get-RequiredDateTimeOffset {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name
    if ($value -is [System.DateTimeOffset]) {
        return $value
    }

    if ($value -is [System.DateTime]) {
        return [System.DateTimeOffset]$value
    }

    $parsed = [System.DateTimeOffset]::MinValue
    if ($value -isnot [string] -or -not [System.DateTimeOffset]::TryParse($value, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) {
        throw "Evidence property '$Name' must be an ISO 8601 timestamp."
    }

    return $parsed
}

function Get-ValidatedCoverageEvidence {
    param(
        [Parameter(Mandatory)][object]$Summary,
        [Parameter(Mandatory)][System.IO.FileInfo]$SummaryFile,
        [Parameter(Mandatory)][System.IO.FileInfo[]]$CoverageFiles,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    $suite = [string](Get-RequiredProperty -Object $Summary -Name 'suite')
    $coverage = Get-RequiredProperty -Object $Summary -Name 'coverage'
    if ($coverage -isnot [System.Management.Automation.PSCustomObject]) {
        throw "Suite '$suite' does not contain coverage metadata."
    }

    $recordedPath = [string](Get-RequiredProperty -Object $coverage -Name 'path')
    if ([System.IO.Path]::GetFileName($recordedPath) -cne 'coverage.opencover.xml' -or [System.IO.Path]::GetFileName([System.IO.Path]::GetDirectoryName($recordedPath)) -cne $suite) {
        throw "Suite '$suite' coverage metadata is not bound to its expected report path."
    }

    if ($SummaryFile.Directory.Name -cne 'evidence' -or $null -eq $SummaryFile.Directory.Parent -or $SummaryFile.Directory.Parent.Name -cne $suite) {
        throw "Suite '$suite' summary is not located in its expected evidence directory."
    }

    $expectedCoveragePath = Join-Path $SummaryFile.Directory.Parent.FullName 'coverage.opencover.xml'
    $matchingCoverage = @($CoverageFiles | Where-Object { $_.FullName -ceq $expectedCoveragePath })
    if ($matchingCoverage.Count -ne 1) {
        throw "Suite '$suite' requires exactly one uploaded coverage report."
    }

    $notBeforeUtc = Get-RequiredDateTimeOffset -Object (Get-RequiredProperty -Object $Summary -Name 'command') -Name 'notBeforeUtc'
    $report = Get-ValidatedOpenCoverReport -CoveragePath $matchingCoverage[0].FullName -NotBeforeUtc $notBeforeUtc -RepositoryRoot $RepositoryRoot
    $actualSha256 = Get-FileSha256 -Path $report.file.FullName
    if (([string](Get-RequiredProperty -Object $coverage -Name 'fileName')) -cne $report.file.Name -or
        ([string](Get-RequiredProperty -Object $coverage -Name 'sha256')) -notmatch '^[0-9a-f]{64}$' -or
        ([string](Get-RequiredProperty -Object $coverage -Name 'sha256')) -cne $actualSha256 -or
        (Get-RequiredNonNegativeLong -Object $coverage -Name 'bytes') -ne $report.file.Length -or
        (Get-RequiredNonNegativeInt -Object $coverage -Name 'moduleCount') -ne $report.moduleCount -or
        (Get-RequiredNonNegativeInt -Object $coverage -Name 'fileCount') -ne $report.fileCount -or
        ([string](Get-RequiredProperty -Object $coverage -Name 'localPathMode')) -cne $report.localPathMode) {
        throw "Suite '$suite' coverage metadata does not match its validated OpenCover report."
    }

    $null = Get-RequiredDateTimeOffset -Object $coverage -Name 'lastWriteUtc'
    return $report
}

function Assert-PassingTrxSummary {
    param(
        [Parameter(Mandatory)]
        [object]$Summary,
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$SummaryFile,
        [Parameter(Mandatory)]
        [System.IO.FileInfo[]]$TrxFiles,
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$BuildManifestFile,
        [Parameter(Mandatory)]
        [object[]]$DiscoveryManifests,
        [Parameter(Mandatory)]
        [System.IO.FileInfo[]]$CoverageFiles,
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $suite = [string](Get-RequiredProperty -Object $Summary -Name 'suite')
    if ((Get-RequiredProperty -Object $Summary -Name 'completeSuite') -isnot [bool] -or -not $Summary.completeSuite) {
        throw "Suite '$suite' is not a complete-suite assertion."
    }

    Assert-ExpectedMergeSha -Document $Summary -Description "Suite '$suite'"
    $buildReference = Get-RequiredProperty -Object $Summary -Name 'buildManifest'
    $expectedBuildSha256 = [string](Get-RequiredProperty -Object $buildReference -Name 'sha256')
    $actualBuildSha256 = (Get-FileHash -LiteralPath $BuildManifestFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedBuildSha256 -cne $actualBuildSha256) {
        throw "Suite '$suite' does not reference the uploaded Release build manifest."
    }

    $matchingDiscovery = @($DiscoveryManifests | Where-Object { $_.document.suite -ceq $suite })
    if ($matchingDiscovery.Count -ne 1) {
        throw "Suite '$suite' requires exactly one uploaded discovery manifest, but found $($matchingDiscovery.Count)."
    }

    $discoveryReference = Get-RequiredProperty -Object $Summary -Name 'discoveryManifest'
    $expectedDiscoverySha256 = [string](Get-RequiredProperty -Object $discoveryReference -Name 'sha256')
    $actualDiscoverySha256 = (Get-FileHash -LiteralPath $matchingDiscovery[0].file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedDiscoverySha256 -cne $actualDiscoverySha256) {
        throw "Suite '$suite' does not reference its uploaded discovery manifest."
    }

    $discovery = $matchingDiscovery[0].document
    Assert-ExpectedMergeSha -Document $discovery -Description "Suite '$suite' discovery"
    if ($discovery.buildHead -cne $MergeSha -or $discovery.identityScheme -cne $discoveryReference.identityScheme -or $discovery.testListSha256 -cne $discoveryReference.testListSha256) {
        throw "Suite '$suite' discovery evidence does not match its TRX summary."
    }

    $command = Get-RequiredProperty -Object $Summary -Name 'command'
    if ([int](Get-RequiredProperty -Object $command -Name 'exitCode') -ne 0) {
        throw "Suite '$suite' records a nonzero command exit code."
    }

    $trx = Get-RequiredProperty -Object $Summary -Name 'trx'
    $fileName = [string](Get-RequiredProperty -Object $trx -Name 'fileName')
    $matchingTrx = @($TrxFiles | Where-Object { $_.Name -ceq $fileName })
    if ($matchingTrx.Count -ne 1) {
        throw "Suite '$suite' requires exactly one uploaded TRX named '$fileName', but found $($matchingTrx.Count)."
    }

    $actualTrxSha256 = (Get-FileHash -LiteralPath $matchingTrx[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([string](Get-RequiredProperty -Object $trx -Name 'sha256') -cne $actualTrxSha256) {
        throw "Suite '$suite' uploaded TRX bytes do not match the asserted SHA-256."
    }

    if ([long](Get-RequiredProperty -Object $trx -Name 'bytes') -ne $matchingTrx[0].Length) {
        throw "Suite '$suite' uploaded TRX byte count does not match its assertion."
    }

    $validatedTrx = Get-ValidatedUploadedTrx -TrxFile $matchingTrx[0]
    $summaryTestNames = Get-RequiredStringArray -Object $trx -Name 'testNames'
    $discoveryTestNames = Get-RequiredStringArray -Object $discovery -Name 'tests'
    $sortedDiscoveryTestNames = Get-SortedOrdinalStrings -Values $discoveryTestNames
    if (-not (Test-StringArraysEqualOrdinal -Left $discoveryTestNames -Right $sortedDiscoveryTestNames) -or (Get-RequiredNonNegativeInt -Object $discovery -Name 'testCount') -ne $discoveryTestNames.Count -or $discovery.testListSha256 -cne (Get-StringListSha256 -Values $discoveryTestNames)) {
        throw "Suite '$suite' uploaded discovery manifest is internally inconsistent."
    }

    if (-not (Test-StringArraysEqualOrdinal -Left $validatedTrx.testNames -Right $summaryTestNames) -or -not (Test-StringArraysEqualOrdinal -Left $validatedTrx.testNames -Right $discoveryTestNames)) {
        throw "Suite '$suite' uploaded TRX test-name multiset does not match its asserted same-SHA evidence."
    }

    $summaryCounters = Get-RequiredProperty -Object $trx -Name 'counters'
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'notExecuted')) {
        if ((Get-RequiredNonNegativeInt -Object $summaryCounters -Name $name) -ne $validatedTrx.counters.$name) {
            throw "Suite '$suite' uploaded TRX '$name' counter does not match its assertion."
        }
    }

    if ((Get-RequiredNonNegativeInt -Object $trx -Name 'testCount') -ne $validatedTrx.testNames.Count) {
        throw "Suite '$suite' uploaded TRX test count does not match its asserted result set."
    }

    $identity = Get-RequiredProperty -Object $Summary -Name 'testIdentity'
    if ($identity.discovery -cne 'vstest display-name multiset' -or $identity.execution -cne 'trx executionId') {
        throw "Suite '$suite' has an unsupported asserted test identity scheme."
    }

    return [pscustomobject]@{
        suite = $suite
        trx = $matchingTrx[0]
        coverage = Get-ValidatedCoverageEvidence -Summary $Summary -SummaryFile $SummaryFile -CoverageFiles $CoverageFiles -RepositoryRoot $RepositoryRoot
    }
}

function Get-ValidatedProvenance {
    $parsedRunId = [long]0
    if ($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$') {
        throw 'Repository must be a GitHub owner/name identifier.'
    }

    if (-not [long]::TryParse($RunId, [ref]$parsedRunId) -or $parsedRunId -le 0) {
        throw 'RunId must be a positive integer.'
    }
    $canonicalRunId = $parsedRunId.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    if ($RunId -cne $canonicalRunId) {
        throw 'RunId must use canonical decimal notation.'
    }

    $parsedRunAttempt = 0
    if (-not [int]::TryParse($RunAttempt, [ref]$parsedRunAttempt) -or $parsedRunAttempt -le 0) {
        throw 'RunAttempt must be a positive integer.'
    }
    $canonicalRunAttempt = $parsedRunAttempt.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    if ($RunAttempt -cne $canonicalRunAttempt) {
        throw 'RunAttempt must use canonical decimal notation.'
    }

    if ($Event -cnotin @('pull_request', 'push')) {
        throw 'Event must be pull_request or push.'
    }

    if ($Event -ceq 'pull_request' -and $Ref -notmatch '^refs/pull/[1-9][0-9]*/merge$') {
        throw 'Pull-request Ref must identify the merge ref.'
    }

    if ($Event -ceq 'push' -and $Ref -notmatch '^refs/heads/[A-Za-z0-9][A-Za-z0-9._/-]*$') {
        throw 'Push Ref must identify a branch ref.'
    }

    if ($MergeSha -notmatch '^[0-9a-f]{40}$') {
        throw 'MergeSha must be a full lowercase Git SHA.'
    }

    if ($Event -ceq 'pull_request' -and $PullRequestHeadSha -notmatch '^[0-9a-f]{40}$') {
        throw 'PullRequestHeadSha must be a full lowercase Git SHA for pull_request events.'
    }

    if ($Event -ceq 'push' -and -not [string]::IsNullOrEmpty($PullRequestHeadSha)) {
        throw 'PullRequestHeadSha must be empty for push events.'
    }

    return [pscustomobject]@{
        repository = $Repository
        runId = $canonicalRunId
        runAttempt = $canonicalRunAttempt
        event = $Event
        ref = $Ref
        mergeSha = $MergeSha
        pullRequestHeadSha = if ([string]::IsNullOrEmpty($PullRequestHeadSha)) { $null } else { $PullRequestHeadSha }
    }
}

function Publish-SonarInputs {
    param(
        [Parameter(Mandatory)][object[]]$SuiteEvidence,
        [Parameter(Mandatory)][object]$Provenance,
        [Parameter(Mandatory)][string]$Destination
    )

    $finalDirectory = [System.IO.Path]::GetFullPath($Destination)
    if (Test-Path -LiteralPath $finalDirectory) {
        throw 'The Sonar inputs directory already exists; refusing to overwrite validated inputs.'
    }

    $parentDirectory = Split-Path -Parent $finalDirectory
    if (-not (Test-Path -LiteralPath $parentDirectory -PathType Container)) {
        $null = New-Item -ItemType Directory -Path $parentDirectory -Force
    }

    $leaf = Split-Path -Leaf $finalDirectory
    $temporaryDirectory = Join-Path $parentDirectory ".$leaf.staging-$([Guid]::NewGuid().ToString('N'))"
    try {
        $null = New-Item -ItemType Directory -Path $temporaryDirectory -ErrorAction Stop
        $testResultsDirectory = New-Item -ItemType Directory -Path (Join-Path $temporaryDirectory 'TestResults') -ErrorAction Stop
        $sonarInputsDirectory = New-Item -ItemType Directory -Path (Join-Path $testResultsDirectory.FullName 'SonarInputs') -ErrorAction Stop
        $manifestSuites = [System.Collections.Generic.List[object]]::new()
        foreach ($entry in $SuiteEvidence) {
            $suiteDirectory = Join-Path $sonarInputsDirectory.FullName $entry.suite
            $null = New-Item -ItemType Directory -Path $suiteDirectory -ErrorAction Stop
            $trxName = "$($entry.suite)-$($Provenance.mergeSha).trx"
            $stagedTrxPath = Join-Path $suiteDirectory $trxName
            $stagedCoveragePath = Join-Path $suiteDirectory 'coverage.opencover.xml'
            Copy-Item -LiteralPath $entry.trx.FullName -Destination $stagedTrxPath -ErrorAction Stop
            Copy-Item -LiteralPath $entry.coverage.file.FullName -Destination $stagedCoveragePath -ErrorAction Stop

            $stagedTrx = Get-Item -LiteralPath $stagedTrxPath
            $stagedCoverage = Get-Item -LiteralPath $stagedCoveragePath
            $trxSha256 = Get-FileSha256 -Path $stagedTrx.FullName
            $coverageSha256 = Get-FileSha256 -Path $stagedCoverage.FullName
            if ($trxSha256 -cne (Get-FileSha256 -Path $entry.trx.FullName) -or $stagedTrx.Length -ne $entry.trx.Length -or
                $coverageSha256 -cne (Get-FileSha256 -Path $entry.coverage.file.FullName) -or $stagedCoverage.Length -ne $entry.coverage.file.Length) {
                throw "Staged Sonar input copy for suite '$($entry.suite)' did not preserve validated bytes."
            }

            $manifestSuites.Add([pscustomobject]([ordered]@{
                        suite = $entry.suite
                        trx = [pscustomobject]([ordered]@{
                                checkoutRelativePath = "TestResults/SonarInputs/$($entry.suite)/$trxName"
                                sha256 = $trxSha256
                                bytes = $stagedTrx.Length
                            })
                        coverage = [pscustomobject]([ordered]@{
                                checkoutRelativePath = "TestResults/SonarInputs/$($entry.suite)/coverage.opencover.xml"
                                sha256 = $coverageSha256
                                bytes = $stagedCoverage.Length
                                moduleCount = $entry.coverage.moduleCount
                                fileCount = $entry.coverage.fileCount
                                localPathMode = $entry.coverage.localPathMode
                            })
                    }))
        }

        $manifest = [pscustomobject]([ordered]@{
                schemaVersion = 1
                kind = 'sonar-inputs'
                repository = $Provenance.repository
                runId = $Provenance.runId
                runAttempt = $Provenance.runAttempt
                event = $Provenance.event
                ref = $Provenance.ref
                mergeSha = $Provenance.mergeSha
                pullRequestHeadSha = $Provenance.pullRequestHeadSha
                suites = @($manifestSuites)
            })
        Write-RedactedJsonFile -Path (Join-Path $temporaryDirectory 'manifest.json') -Value $manifest -NoClobber
        [System.IO.Directory]::Move($temporaryDirectory, $finalDirectory)
        $temporaryDirectory = $null
    }
    finally {
        if ($null -ne $temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction Stop
            if (Test-Path -LiteralPath $temporaryDirectory) {
                throw 'The incomplete Sonar input staging directory could not be removed.'
            }
        }
    }
}

$provenance = Get-ValidatedProvenance
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$root = [System.IO.Path]::GetFullPath($EvidenceRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw 'The downloaded verification artifact directory is missing.'
}

$documents = [System.Collections.Generic.List[object]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $root -Filter '*.json' -File -Recurse)) {
    if ($file.Length -eq 0) {
        throw "Evidence JSON '$($file.FullName)' is empty."
    }

    try {
        $document = [System.IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json -Depth 32
    }
    catch {
        throw "Evidence JSON '$($file.FullName)' is malformed."
    }

    if ($document -isnot [System.Management.Automation.PSCustomObject]) {
        throw "Evidence JSON '$($file.FullName)' must contain an object."
    }

    $documents.Add([pscustomobject]@{ file = $file; document = $document })
}

$buildManifests = @($documents | Where-Object {
    $_.document.PSObject.Properties['kind'] -and $_.document.kind -ceq 'release-build'
})
if ($buildManifests.Count -ne 1) {
    throw "Expected exactly one uploaded Release build manifest, but found $($buildManifests.Count)."
}

$build = $buildManifests[0].document
Assert-ExpectedMergeSha -Document $build -Description 'Release build manifest'
if ($build.configuration -cne 'Release' -or [int]$build.exitCode -ne 0) {
    throw 'The uploaded build manifest is not a successful Release build.'
}

$expectedSuites = @('Unit', 'Architecture', 'InMemoryIntegration', 'PostgreSqlIntegration', 'DataSeeder')
$summaries = @($documents | Where-Object {
    $_.document.PSObject.Properties['kind'] -and $_.document.kind -ceq 'trx-summary'
})
if ($summaries.Count -ne $expectedSuites.Count) {
    throw "Expected $($expectedSuites.Count) uploaded TRX summaries, but found $($summaries.Count)."
}

$actualSuites = @($summaries | ForEach-Object { [string]$_.document.suite } | Sort-Object)
if (($actualSuites -join "`n") -cne (($expectedSuites | Sort-Object) -join "`n")) {
    throw "The uploaded TRX summary suite set is incomplete or unexpected: $($actualSuites -join ', ')."
}

$discoveryManifests = @($documents | Where-Object {
    $_.document.PSObject.Properties['kind'] -and $_.document.kind -ceq 'test-discovery'
})
if ($discoveryManifests.Count -ne $expectedSuites.Count) {
    throw "Expected $($expectedSuites.Count) uploaded discovery manifests, but found $($discoveryManifests.Count)."
}

$actualDiscoverySuites = @($discoveryManifests | ForEach-Object { [string]$_.document.suite } | Sort-Object)
if (($actualDiscoverySuites -join "`n") -cne (($expectedSuites | Sort-Object) -join "`n")) {
    throw "The uploaded discovery manifest suite set is incomplete or unexpected: $($actualDiscoverySuites -join ', ')."
}

$trxFiles = @(Get-ChildItem -LiteralPath $root -Filter '*.trx' -File -Recurse)
$coverageFiles = @(Get-ChildItem -LiteralPath $root -Filter 'coverage.opencover.xml' -File -Recurse)
if ($coverageFiles.Count -ne $expectedSuites.Count) {
    throw "Expected $($expectedSuites.Count) uploaded OpenCover reports, but found $($coverageFiles.Count)."
}

$suiteEvidence = [System.Collections.Generic.List[object]]::new()
foreach ($summary in $summaries | Sort-Object { [string]$_.document.suite }) {
    $suiteEvidence.Add((Assert-PassingTrxSummary -Summary $summary.document -SummaryFile $summary.file -TrxFiles $trxFiles -BuildManifestFile $buildManifests[0].file -DiscoveryManifests $discoveryManifests -CoverageFiles $coverageFiles -RepositoryRoot $repositoryRoot))
}

$cleanupProbes = @($documents | Where-Object {
    $_.document.PSObject.Properties['kind'] -and $_.document.kind -ceq 'postgresql-cleanup'
})
if ($cleanupProbes.Count -ne 1) {
    throw "Expected exactly one PostgreSQL cleanup probe, but found $($cleanupProbes.Count)."
}

$cleanup = $cleanupProbes[0].document
if ($cleanup.outcome -cne 'Passed' -or $cleanup.head -cne $MergeSha) {
    throw 'The PostgreSQL cleanup probe does not prove success for the required SHA.'
}

if (-not [string]::IsNullOrWhiteSpace($SonarInputsDirectory)) {
    Publish-SonarInputs -SuiteEvidence $suiteEvidence.ToArray() -Provenance $provenance -Destination $SonarInputsDirectory
}

Write-Host "Same-SHA CI evidence passed: build=1, suites=$($expectedSuites.Count), cleanup=1, sha=$MergeSha."
