param(
    [Parameter(Mandatory)]
    [string]$DownloadRoot,
    [Parameter(Mandatory)]
    [string]$CheckoutRoot,
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
    [string]$PullRequestHeadSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "VerificationEvidence.psm1") -Force

$ExpectedSuites = @("Unit", "Architecture", "InMemoryIntegration", "PostgreSqlIntegration", "DataSeeder")
$MinimumFreshnessUtc = [System.DateTimeOffset]::UtcNow.AddHours(-24)

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileSystemInfo]$Item,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne [System.IO.FileAttributes]::None) {
        throw "$Description cannot be a symbolic link, junction, or other reparse point."
    }
}

function Get-RequiredDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description is missing."
    }

    $item = Get-Item -LiteralPath $Path
    Assert-NotReparsePoint -Item $item -Description $Description
    return $item
}

function Get-RequiredFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing."
    }

    $item = Get-Item -LiteralPath $Path
    Assert-NotReparsePoint -Item $item -Description $Description
    return $item
}

function Get-ArtifactPath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match "(^|[\\/])\.{1,2}([\\/]|$)") {
        throw "Artifact relative path '$RelativePath' is not confined."
    }

    $path = $Root
    foreach ($segment in ($RelativePath -split "[\\/]+")) {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            throw "Artifact relative path '$RelativePath' is malformed."
        }
        $path = Join-Path $path $segment
    }

    return $path
}

function Assert-ExactDirectoryContents {
    param(
        [Parameter(Mandatory)]
        [System.IO.DirectoryInfo]$Directory,
        [Parameter(Mandatory)]
        [string[]]$ExpectedNames,
        [Parameter(Mandatory)]
        [string]$Description
    )

    Assert-NotReparsePoint -Item $Directory -Description $Description
    $actualItems = @(Get-ChildItem -LiteralPath $Directory.FullName -Force)
    if ($actualItems.Count -ne $ExpectedNames.Count) {
        throw "$Description must contain exactly $($ExpectedNames.Count) entries, but contains $($actualItems.Count)."
    }

    $actualNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($item in $actualItems) {
        Assert-NotReparsePoint -Item $item -Description "$Description entry '$($item.Name)'"
        if (-not $actualNames.Add($item.Name)) {
            throw "$Description contains a duplicate entry named '$($item.Name)'."
        }
    }

    foreach ($expectedName in $ExpectedNames) {
        if (-not $actualNames.Contains($expectedName)) {
            throw "$Description is missing required entry '$expectedName'."
        }
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string[]]$Names,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if ($Object -isnot [System.Management.Automation.PSCustomObject]) {
        throw "$Description must be a JSON object."
    }

    $properties = @($Object.PSObject.Properties)
    if ($properties.Count -ne $Names.Count) {
        throw "$Description has an unexpected property count."
    }

    $actualNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($property in $properties) {
        if (-not $actualNames.Add($property.Name)) {
            throw "$Description contains duplicate property '$($property.Name)'."
        }
    }

    foreach ($name in $Names) {
        if (-not $actualNames.Contains($name)) {
            throw "$Description is missing required property '$name'."
        }
    }
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Description
    )

    $value = $Object.$Name
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "$Description property '$Name' must be a non-empty string."
    }

    return $value
}

function Get-RequiredPositiveInt64 {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Description
    )

    $value = $Object.$Name
    if (($value -isnot [long] -and $value -isnot [int]) -or $value -le 0) {
        throw "$Description property '$Name' must be a positive JSON integer."
    }

    return [long]$value
}

function Assert-Sha {
    param(
        [Parameter(Mandatory)]
        [string]$Sha,
        [Parameter(Mandatory)]
        [string]$Description,
        [int]$Length = 40
    )

    if ($Sha -notmatch "^[0-9a-f]{$Length}$") {
        throw "$Description must be a lowercase $Length-character Git or SHA-256 hash."
    }
}

function Assert-ExactString {
    param(
        [Parameter(Mandatory)]
        [string]$Actual,
        [Parameter(Mandatory)]
        [string]$Expected,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if ($Actual -cne $Expected) {
        throw "$Description does not match the required value."
    }
}

function Assert-ManifestProvenance {
    param(
        [Parameter(Mandatory)]
        [object]$Manifest
    )

    Assert-ExactProperties -Object $Manifest -Names @("schemaVersion", "kind", "repository", "runId", "runAttempt", "event", "ref", "mergeSha", "pullRequestHeadSha", "suites") -Description "Sonar input manifest"
    if (($Manifest.schemaVersion -isnot [long] -and $Manifest.schemaVersion -isnot [int]) -or $Manifest.schemaVersion -ne 1) {
        throw "Sonar input manifest schemaVersion must be the integer 1."
    }

    Assert-ExactString -Actual (Get-RequiredString -Object $Manifest -Name "kind" -Description "Sonar input manifest") -Expected "sonar-inputs" -Description "Sonar input manifest kind"
    Assert-ExactString -Actual (Get-RequiredString -Object $Manifest -Name "repository" -Description "Sonar input manifest") -Expected $Repository -Description "Sonar input manifest repository"
    Assert-ExactString -Actual (Get-RequiredString -Object $Manifest -Name "runId" -Description "Sonar input manifest") -Expected $RunId -Description "Sonar input manifest run ID"
    Assert-ExactString -Actual (Get-RequiredString -Object $Manifest -Name "runAttempt" -Description "Sonar input manifest") -Expected $RunAttempt -Description "Sonar input manifest run attempt"
    Assert-ExactString -Actual (Get-RequiredString -Object $Manifest -Name "event" -Description "Sonar input manifest") -Expected $Event -Description "Sonar input manifest event"
    Assert-ExactString -Actual (Get-RequiredString -Object $Manifest -Name "ref" -Description "Sonar input manifest") -Expected $Ref -Description "Sonar input manifest ref"

    $manifestMergeSha = Get-RequiredString -Object $Manifest -Name "mergeSha" -Description "Sonar input manifest"
    Assert-Sha -Sha $manifestMergeSha -Description "Sonar input manifest merge SHA"
    Assert-ExactString -Actual $manifestMergeSha -Expected $MergeSha -Description "Sonar input manifest merge SHA"

    if ([string]::IsNullOrEmpty($PullRequestHeadSha)) {
        if ($null -ne $Manifest.pullRequestHeadSha) {
            throw "Sonar input manifest PR head SHA must be null when the expected PR head SHA is null."
        }
    }
    else {
        $manifestPrHeadSha = Get-RequiredString -Object $Manifest -Name "pullRequestHeadSha" -Description "Sonar input manifest"
        Assert-Sha -Sha $manifestPrHeadSha -Description "Sonar input manifest PR head SHA"
        Assert-ExactString -Actual $manifestPrHeadSha -Expected $PullRequestHeadSha -Description "Sonar input manifest PR head SHA"
    }
}

function Assert-ReportIntegrity {
    param(
        [Parameter(Mandatory)]
        [object]$Report,
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$File,
        [Parameter(Mandatory)]
        [string]$ExpectedPath,
        [Parameter(Mandatory)]
        [string]$Description,
        [switch]$Coverage
    )

    $properties = if ($Coverage) { @("checkoutRelativePath", "sha256", "bytes", "moduleCount", "fileCount", "localPathMode") } else { @("checkoutRelativePath", "sha256", "bytes") }
    Assert-ExactProperties -Object $Report -Names $properties -Description $Description
    Assert-ExactString -Actual (Get-RequiredString -Object $Report -Name "checkoutRelativePath" -Description $Description) -Expected $ExpectedPath -Description "$Description checkout-relative path"
    if ($Coverage) {
        Assert-ExactString -Actual (Get-RequiredString -Object $Report -Name "localPathMode" -Description $Description) -Expected "repository-rooted" -Description "$Description local path mode"
    }
    $expectedHash = Get-RequiredString -Object $Report -Name "sha256" -Description $Description
    Assert-Sha -Sha $expectedHash -Description "$Description SHA-256" -Length 64
    $expectedBytes = Get-RequiredPositiveInt64 -Object $Report -Name "bytes" -Description $Description
    $actualHash = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHash -cne $actualHash -or $expectedBytes -ne $File.Length) {
        throw "$Description hash or byte count does not match its downloaded report."
    }
}

function Assert-CurrentCheckoutSources {
    param(
        [Parameter(Mandatory)]
        [string[]]$SourcePaths,
        [Parameter(Mandatory)]
        [string]$CheckoutPath
    )

    $checkoutWithSeparator = $CheckoutPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($sourcePath in $SourcePaths) {
        if (-not [System.IO.Path]::IsPathRooted($sourcePath) -or
            -not $sourcePath.StartsWith($checkoutWithSeparator, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "A validated OpenCover source does not resolve under the current checkout."
        }
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($Repository) -or
        $RunId -notmatch "^[1-9][0-9]*$" -or
        $RunAttempt -notmatch "^[1-9][0-9]*$" -or
        [string]::IsNullOrWhiteSpace($Event) -or
        [string]::IsNullOrWhiteSpace($Ref)) {
        throw "Repository, run ID, run attempt, event, and ref must be explicitly supplied."
    }
    Assert-Sha -Sha $MergeSha -Description "Expected merge SHA"
    if (-not [string]::IsNullOrEmpty($PullRequestHeadSha)) {
        Assert-Sha -Sha $PullRequestHeadSha -Description "Expected PR head SHA"
    }

    $download = Get-RequiredDirectory -Path ([System.IO.Path]::GetFullPath($DownloadRoot)) -Description "Downloaded Sonar artifact root"
    $checkout = Get-RequiredDirectory -Path ([System.IO.Path]::GetFullPath($CheckoutRoot)) -Description "Current checkout root"
    $checkoutHeadLines = @(& git -C $checkout.FullName rev-parse HEAD 2>$null)
    $checkoutHead = ($checkoutHeadLines | ForEach-Object { [string]$_ } | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not resolve the current checkout HEAD."
    }
    Assert-Sha -Sha $checkoutHead -Description "Current checkout HEAD"
    Assert-ExactString -Actual $checkoutHead -Expected $MergeSha -Description "Current checkout HEAD"

    Assert-ExactDirectoryContents -Directory $download -ExpectedNames @("manifest.json", "TestResults") -Description "Downloaded Sonar artifact root"
    $manifestFile = Get-RequiredFile -Path (Join-Path $download.FullName "manifest.json") -Description "Downloaded Sonar manifest"
    if ($manifestFile.Length -eq 0) {
        throw "Downloaded Sonar manifest is empty."
    }
    try {
        $manifest = [System.IO.File]::ReadAllText($manifestFile.FullName) | ConvertFrom-Json -Depth 16
    }
    catch {
        throw "Downloaded Sonar manifest is malformed JSON."
    }
    Assert-ManifestProvenance -Manifest $manifest

    if ($manifest.suites -is [string] -or $manifest.suites -isnot [System.Collections.IEnumerable]) {
        throw "Sonar input manifest suites must be an array."
    }
    $suiteEntries = @($manifest.suites)
    if ($suiteEntries.Count -ne $ExpectedSuites.Count) {
        throw "Sonar input manifest must contain exactly $($ExpectedSuites.Count) suites."
    }

    $suiteByName = @{}
    foreach ($entry in $suiteEntries) {
        Assert-ExactProperties -Object $entry -Names @("suite", "trx", "coverage") -Description "Sonar input suite entry"
        $suiteName = Get-RequiredString -Object $entry -Name "suite" -Description "Sonar input suite entry"
        if (-not $ExpectedSuites.Contains($suiteName) -or $suiteByName.ContainsKey($suiteName)) {
            throw "Sonar input manifest suite set is incomplete, unexpected, or duplicated."
        }
        $suiteByName[$suiteName] = $entry
    }
    foreach ($suite in $ExpectedSuites) {
        if (-not $suiteByName.ContainsKey($suite)) {
            throw "Sonar input manifest is missing expected suite '$suite'."
        }
    }

    $testResults = Get-RequiredDirectory -Path (Join-Path $download.FullName "TestResults") -Description "Downloaded TestResults directory"
    Assert-ExactDirectoryContents -Directory $testResults -ExpectedNames @("SonarInputs") -Description "Downloaded TestResults directory"
    $sonarInputs = Get-RequiredDirectory -Path (Join-Path $testResults.FullName "SonarInputs") -Description "Downloaded SonarInputs directory"
    Assert-ExactDirectoryContents -Directory $sonarInputs -ExpectedNames $ExpectedSuites -Description "Downloaded SonarInputs directory"

    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($suite in $ExpectedSuites) {
        $entry = $suiteByName[$suite]
        $suiteDirectory = Get-RequiredDirectory -Path (Join-Path $sonarInputs.FullName $suite) -Description "Downloaded suite '$suite' directory"
        $trxRelativePath = "TestResults/SonarInputs/$suite/$suite-$MergeSha.trx"
        $coverageRelativePath = "TestResults/SonarInputs/$suite/coverage.opencover.xml"
        Assert-ExactDirectoryContents -Directory $suiteDirectory -ExpectedNames @("$suite-$MergeSha.trx", "coverage.opencover.xml") -Description "Downloaded suite '$suite' directory"
        $trxFile = Get-RequiredFile -Path (Get-ArtifactPath -Root $download.FullName -RelativePath $trxRelativePath) -Description "Downloaded suite '$suite' TRX"
        $coverageFile = Get-RequiredFile -Path (Get-ArtifactPath -Root $download.FullName -RelativePath $coverageRelativePath) -Description "Downloaded suite '$suite' OpenCover report"
        Assert-ReportIntegrity -Report $entry.trx -File $trxFile -ExpectedPath $trxRelativePath -Description "Suite '$suite' TRX"
        Assert-ReportIntegrity -Report $entry.coverage -File $coverageFile -ExpectedPath $coverageRelativePath -Description "Suite '$suite' OpenCover report" -Coverage

        $trx = Get-ValidatedTrx -TrxDirectory $suiteDirectory.FullName -ExpectedTrxFileName "$suite-$MergeSha.trx" -NotBeforeUtc $MinimumFreshnessUtc
        $coverage = Get-ValidatedOpenCoverReport -CoveragePath $coverageFile.FullName -NotBeforeUtc $MinimumFreshnessUtc -RepositoryRoot $checkout.FullName
        $expectedModuleCount = Get-RequiredPositiveInt64 -Object $entry.coverage -Name "moduleCount" -Description "Suite '$suite' OpenCover report"
        $expectedFileCount = Get-RequiredPositiveInt64 -Object $entry.coverage -Name "fileCount" -Description "Suite '$suite' OpenCover report"
        if ($coverage.moduleCount -ne $expectedModuleCount -or $coverage.fileCount -ne $expectedFileCount) {
            throw "Suite '$suite' OpenCover module or file count does not match its manifest."
        }
        Assert-CurrentCheckoutSources -SourcePaths $coverage.sourcePaths -CheckoutPath $checkout.FullName
        $records.Add("SONAR_INPUT suite=$suite coveragePath=$($coverage.file.FullName) sha256=$((Get-FileHash -LiteralPath $coverage.file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) bytes=$($coverage.file.Length) modules=$($coverage.moduleCount) files=$($coverage.fileCount)")
    }

    foreach ($record in $records) {
        Write-Output $record
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
