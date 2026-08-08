Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-AbsolutePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Protect-EvidenceString {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    $redacted = $Value
    $redacted = $redacted -replace '(?i)\b(password|pwd|secret|token|credential|apikey|api_key|access[_ ]?key|private[_ ]?key|username|user id|uid)\s*=\s*([^;\s''"]+)', '$1=[redacted]'
    $redacted = $redacted -replace '(?i)(authorization\s*:\s*(?:bearer|basic)\s+)\S+', '$1[redacted]'
    $redacted = $redacted -replace '(?i)(https?://)[^/@\s:]+:[^/@\s]+@', '$1[redacted]@'
    $redacted = $redacted -replace '(?i)("(?:password|pwd|secret|token|credential|apiKey|accessKey)"\s*:\s*")[^"]*(")', '$1[redacted]$2'

    return $redacted
}

function Test-SensitiveEvidencePropertyName {
    param(
        [AllowEmptyString()]
        [string]$Name
    )

    return $Name -match '(?i)(connection.?string|password|pwd|secret|token|credential|authorization|api.?key|access.?key|private.?key|username|user.?id|uid)'
}

function Protect-EvidenceValue {
    param(
        [AllowNull()]
        [object]$Value,
        [string]$PropertyName = ""
    )

    if (Test-SensitiveEvidencePropertyName -Name $PropertyName) {
        return "[redacted]"
    }

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [string]) {
        return Protect-EvidenceString -Value $Value
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $redactedDictionary = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $name = [string]$key
            $redactedDictionary[$name] = Protect-EvidenceValue -Value $Value[$key] -PropertyName $name
        }

        return [pscustomobject]$redactedDictionary
    }

    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $redactedObject = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $redactedObject[$property.Name] = Protect-EvidenceValue -Value $property.Value -PropertyName $property.Name
        }

        return [pscustomobject]$redactedObject
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $redactedItems = [System.Collections.Generic.List[object]]::new()
        foreach ($item in $Value) {
            $redactedItems.Add((Protect-EvidenceValue -Value $item))
        }

        return ,($redactedItems.ToArray())
    }

    return $Value
}

function Write-RedactedJsonFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [object]$Value,
        [switch]$NoClobber
    )

    $fullPath = ConvertTo-AbsolutePath -Path $Path
    $parent = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw "The evidence parent directory does not exist."
    }

    $json = (Protect-EvidenceValue -Value $Value | ConvertTo-Json -Depth 32) + [Environment]::NewLine
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $mode = if ($NoClobber) { [System.IO.FileMode]::CreateNew } else { [System.IO.FileMode]::Create }
    $stream = [System.IO.File]::Open($fullPath, $mode, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $writer = [System.IO.StreamWriter]::new($stream, $encoding)
        try {
            $writer.Write($json)
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-FileSha256 {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The file required for evidence hashing does not exist."
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-StringListSha256 {
    param(
        [Parameter(Mandatory)]
        [string[]]$Values
    )

    $payload = ([string]::Join("`n", $Values) + "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-SortedOrdinalStrings {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Values
    )

    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $Values) {
        $list.Add($value)
    }

    $list.Sort([System.StringComparer]::Ordinal)
    $sorted = $list.ToArray()
    return ,$sorted
}

function Test-StringArraysEqualOrdinal {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Left,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Right
    )

    if ($Left.Count -ne $Right.Count) {
        return $false
    }

    for ($index = 0; $index -lt $Left.Count; $index++) {
        if ($Left[$index] -cne $Right[$index]) {
            return $false
        }
    }

    return $true
}

function Get-ObjectProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $properties = @($Object.PSObject.Properties | Where-Object { $_.Name -ceq $Name })
    if ($properties.Count -ne 1) {
        throw "The evidence document is missing required property '$Name'."
    }

    return $properties[0].Value
}

function Get-RequiredArrayProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $properties = @($Object.PSObject.Properties | Where-Object { $_.Name -ceq $Name })
    if ($properties.Count -ne 1) {
        throw "The evidence document is missing required property '$Name'."
    }

    $value = $properties[0].Value
    if ($value -is [string] -or $value -isnot [System.Collections.IEnumerable]) {
        throw "The evidence document property '$Name' must be an array."
    }

    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($item in $value) {
        $items.Add($item)
    }

    return ,$items
}

function Get-RequiredStringProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = Get-ObjectProperty -Object $Object -Name $Name
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "The evidence document property '$Name' must be a non-empty string."
    }

    return $value
}

function Get-RequiredBooleanProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = Get-ObjectProperty -Object $Object -Name $Name
    if ($value -isnot [bool]) {
        throw "The evidence document property '$Name' must be a boolean."
    }

    return $value
}

function Get-RequiredIntProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = Get-ObjectProperty -Object $Object -Name $Name
    $parsed = 0
    if (-not [int]::TryParse([string]$value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        throw "The evidence document property '$Name' must be an integer."
    }

    return $parsed
}

function Get-RequiredDateTimeOffsetProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = Get-ObjectProperty -Object $Object -Name $Name
    if ($value -is [System.DateTimeOffset]) {
        return $value
    }

    if ($value -is [System.DateTime]) {
        return [System.DateTimeOffset]$value
    }

    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "The evidence document property '$Name' must be an ISO 8601 timestamp."
    }

    $parsed = [System.DateTimeOffset]::MinValue
    if (-not [System.DateTimeOffset]::TryParse($value, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) {
        throw "The evidence document property '$Name' must be an ISO 8601 timestamp."
    }

    return $parsed
}

function Get-RequiredObjectProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = Get-ObjectProperty -Object $Object -Name $Name
    if ($value -isnot [System.Management.Automation.PSCustomObject]) {
        throw "The evidence document property '$Name' must be an object."
    }

    return $value
}

function Get-RequiredStringArrayProperty {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string]$Name,
        [switch]$RequireNonEmpty
    )

    $value = Get-RequiredArrayProperty -Object $Object -Name $Name

    $items = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $value) {
        if ($item -isnot [string] -or [string]::IsNullOrWhiteSpace($item)) {
            throw "The evidence document property '$Name' must contain only non-empty strings."
        }

        if ($item.Contains("`r") -or $item.Contains("`n")) {
            throw "The evidence document property '$Name' cannot contain multiline entries."
        }

        $items.Add($item)
    }

    if ($RequireNonEmpty -and $items.Count -eq 0) {
        throw "The evidence document property '$Name' cannot be empty."
    }

    $result = $items.ToArray()
    return ,$result
}

function Read-EvidenceJson {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$DocumentName
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The $DocumentName evidence file is missing."
    }

    if ((Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "The $DocumentName evidence file is empty."
    }

    try {
        $document = [System.IO.File]::ReadAllText((ConvertTo-AbsolutePath -Path $Path)) | ConvertFrom-Json -Depth 32
    }
    catch {
        throw "The $DocumentName evidence file is malformed JSON."
    }

    if ($document -isnot [System.Management.Automation.PSCustomObject]) {
        throw "The $DocumentName evidence file must contain a JSON object."
    }

    return $document
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = @(& git -C $RepositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the repository state required for verification."
    }

    return (($output | ForEach-Object { [string]$_ }) -join "`n").Trim()
}

function Get-RepositorySnapshot {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $root = ConvertTo-AbsolutePath -Path $RepositoryRoot
    if (-not (Test-Path -LiteralPath (Join-Path $root ".git"))) {
        throw "The verification repository root is not a Git worktree."
    }

    $head = Invoke-GitText -RepositoryRoot $root -Arguments @("rev-parse", "HEAD")
    if ($head -notmatch '^[0-9a-f]{40}$') {
        throw "The verification repository does not have a valid HEAD SHA."
    }

    $branch = Invoke-GitText -RepositoryRoot $root -Arguments @("branch", "--show-current")
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = "(detached)"
    }

    $statusText = Invoke-GitText -RepositoryRoot $root -Arguments @("status", "--porcelain=v1", "--untracked-files=all")
    $status = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($statusText)) {
        foreach ($line in ($statusText -split "`r?`n")) {
            $status.Add($line)
        }
    }

    $statusValues = $status.ToArray()

    return [pscustomobject]@{
        path = $root
        branch = $branch
        head = $head
        worktree = [pscustomobject]@{
            isClean = $statusValues.Count -eq 0
            status = $statusValues
        }
    }
}

function Assert-CleanRepositorySnapshot {
    param(
        [Parameter(Mandatory)]
        [object]$Snapshot
    )

    if (-not $Snapshot.worktree.isClean -or $Snapshot.worktree.status.Count -ne 0) {
        throw "The verification repository worktree is dirty. Same-SHA evidence requires a clean worktree."
    }
}

function Assert-ManifestRepositoryIdentity {
    param(
        [Parameter(Mandatory)]
        [object]$Manifest,
        [Parameter(Mandatory)]
        [object]$Snapshot,
        [Parameter(Mandatory)]
        [string]$ExpectedHead
    )

    $repository = Get-RequiredObjectProperty -Object $Manifest -Name "repository"
    $manifestPath = ConvertTo-AbsolutePath -Path (Get-RequiredStringProperty -Object $repository -Name "path")
    if ($manifestPath -cne $Snapshot.path) {
        throw "The evidence manifest repository path does not match the active repository."
    }

    if ((Get-RequiredStringProperty -Object $repository -Name "branch") -cne $Snapshot.branch) {
        throw "The evidence manifest branch does not match the active repository."
    }

    $manifestHead = Get-RequiredStringProperty -Object $repository -Name "head"
    if ($manifestHead -cne $ExpectedHead -or $manifestHead -cne $Snapshot.head) {
        throw "The evidence manifest SHA does not match the active repository HEAD."
    }

    $manifestWorktree = Get-RequiredObjectProperty -Object $repository -Name "worktree"
    if (-not (Get-RequiredBooleanProperty -Object $manifestWorktree -Name "isClean")) {
        throw "The evidence manifest records a dirty worktree."
    }

    $manifestStatus = Get-RequiredStringArrayProperty -Object $manifestWorktree -Name "status"
    if ($manifestStatus.Count -ne 0) {
        throw "The evidence manifest records worktree changes."
    }

    Assert-CleanRepositorySnapshot -Snapshot $Snapshot
}

function Assert-RecordedCommands {
    param(
        [Parameter(Mandatory)]
        [object]$Manifest
    )

    $commands = Get-RequiredArrayProperty -Object $Manifest -Name "commands"

    $count = 0
    foreach ($command in $commands) {
        if ($command -isnot [System.Management.Automation.PSCustomObject]) {
            throw "The evidence manifest contains an invalid command record."
        }

        $null = Get-RequiredStringArrayProperty -Object $command -Name "arguments" -RequireNonEmpty
        $exitCode = Get-RequiredIntProperty -Object $command -Name "exitCode"
        if ($exitCode -ne 0) {
            throw "The evidence manifest contains a command that exited nonzero."
        }

        $count++
    }

    if ($count -eq 0) {
        throw "The evidence manifest must record at least one command."
    }
}

function Assert-ArtifactEnvelope {
    param(
        [Parameter(Mandatory)]
        [object]$Manifest,
        [Parameter(Mandatory)]
        [string]$Kind,
        [Parameter(Mandatory)]
        [object]$Snapshot,
        [Parameter(Mandatory)]
        [string]$ExpectedHead
    )

    if ((Get-RequiredIntProperty -Object $Manifest -Name "schemaVersion") -ne 1) {
        throw "The evidence manifest schema version is unsupported."
    }

    if ((Get-RequiredStringProperty -Object $Manifest -Name "kind") -cne $Kind) {
        throw "The evidence manifest kind is invalid."
    }

    Assert-ManifestRepositoryIdentity -Manifest $Manifest -Snapshot $Snapshot -ExpectedHead $ExpectedHead
    $startedUtc = Get-RequiredDateTimeOffsetProperty -Object $Manifest -Name "startedUtc"
    $completedUtc = Get-RequiredDateTimeOffsetProperty -Object $Manifest -Name "completedUtc"
    if ($completedUtc -lt $startedUtc) {
        throw "The evidence manifest timestamps are out of order."
    }

    if ((Get-RequiredIntProperty -Object $Manifest -Name "exitCode") -ne 0) {
        throw "The evidence manifest records a nonzero exit code."
    }

    Assert-RecordedCommands -Manifest $Manifest
}

function Get-DiscoveryManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$SuiteName,
        [Parameter(Mandatory)]
        [object]$Snapshot,
        [Parameter(Mandatory)]
        [string]$ExpectedHead,
        [Parameter(Mandatory)]
        [object]$BuildCompletedUtc
    )

    $manifest = Read-EvidenceJson -Path $Path -DocumentName "test discovery"
    Assert-ArtifactEnvelope -Manifest $manifest -Kind "test-discovery" -Snapshot $Snapshot -ExpectedHead $ExpectedHead
    if ((Get-RequiredStringProperty -Object $manifest -Name "suite") -cne $SuiteName) {
        throw "The discovery manifest suite does not match the requested suite."
    }

    if ((Get-RequiredStringProperty -Object $manifest -Name "buildHead") -cne $ExpectedHead) {
        throw "The discovery manifest was not generated from the required Release build SHA."
    }

    $startedUtc = Get-RequiredDateTimeOffsetProperty -Object $manifest -Name "startedUtc"
    if ($startedUtc -lt $BuildCompletedUtc) {
        throw "The discovery manifest predates the required Release build."
    }

    $tests = Get-RequiredStringArrayProperty -Object $manifest -Name "tests" -RequireNonEmpty
    $sortedTests = Get-SortedOrdinalStrings -Values $tests
    if (-not (Test-StringArraysEqualOrdinal -Left $tests -Right $sortedTests)) {
        throw "The discovery manifest test names are not ordinal-sorted."
    }

    if ((Get-RequiredIntProperty -Object $manifest -Name "testCount") -ne $tests.Count) {
        throw "The discovery manifest test count does not match its test names."
    }

    if ((Get-RequiredStringProperty -Object $manifest -Name "identityScheme") -cne "vstest-display-name-multiset-utf8-v1") {
        throw "The discovery manifest identity scheme is unsupported."
    }

    if ((Get-RequiredStringProperty -Object $manifest -Name "testListSha256") -cne (Get-StringListSha256 -Values $tests)) {
        throw "The discovery manifest test-name hash does not match its test names."
    }

    $filters = Get-RequiredStringArrayProperty -Object $manifest -Name "declaredFilters"
    return [pscustomobject]@{
        manifest = $manifest
        tests = $tests
        filters = $filters
    }
}

function Get-ReleaseBuildManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [object]$Snapshot,
        [Parameter(Mandatory)]
        [string]$ExpectedHead
    )

    $manifest = Read-EvidenceJson -Path $Path -DocumentName "Release build"
    Assert-ArtifactEnvelope -Manifest $manifest -Kind "release-build" -Snapshot $Snapshot -ExpectedHead $ExpectedHead
    if ((Get-RequiredStringProperty -Object $manifest -Name "configuration") -cne "Release") {
        throw "The evidence manifest is not a Release build."
    }

    return [pscustomobject]@{
        manifest = $manifest
        completedUtc = Get-RequiredDateTimeOffsetProperty -Object $manifest -Name "completedUtc"
    }
}

function Get-CommandFilters {
    param(
        [Parameter(Mandatory)]
        [string[]]$Command
    )

    $filters = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Command.Count; $index++) {
        $argument = $Command[$index]
        if ($argument -ceq "--filter" -or $argument -ceq "-filter") {
            if (($index + 1) -ge $Command.Count -or [string]::IsNullOrWhiteSpace($Command[$index + 1])) {
                throw "The test command contains an empty filter."
            }

            $filters.Add($Command[$index + 1])
            $index++
            continue
        }

        if ($argument -cmatch '^--filter=(.+)$') {
            $filters.Add($Matches[1])
            continue
        }

        if ($argument -cmatch '^/TestCaseFilter:(.+)$') {
            $filters.Add($Matches[1])
        }
    }

    $result = $filters.ToArray()
    return ,$result
}

function Assert-CommandMatchesDiscovery {
    param(
        [Parameter(Mandatory)]
        [string[]]$Command,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$DeclaredFilters,
        [switch]$Supplementary
    )

    if ($Command.Count -eq 0) {
        throw "The test command is missing from the evidence request."
    }

    $actualFilters = Get-CommandFilters -Command $Command
    if (-not (Test-StringArraysEqualOrdinal -Left $actualFilters -Right $DeclaredFilters)) {
        throw "The test command filters do not match the declared discovery filters."
    }

    if (-not $Supplementary -and $actualFilters.Count -gt 0 -and $DeclaredFilters.Count -eq 0) {
        throw "A filtered test command cannot satisfy a full-suite declaration."
    }
}

function Get-RequiredTrxCounter {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlElement]$Counters,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = $Counters.GetAttribute($Name)
    $parsed = 0
    if ([string]::IsNullOrWhiteSpace($value) -or -not [int]::TryParse($value, [ref]$parsed) -or $parsed -lt 0) {
        throw "The TRX result does not contain a valid '$Name' counter."
    }

    return $parsed
}

function Get-ValidatedTrx {
    param(
        [Parameter(Mandatory)]
        [string]$TrxDirectory,
        [string]$ExpectedTrxFileName = "",
        [Parameter(Mandatory)]
        [System.DateTimeOffset]$NotBeforeUtc
    )

    if (-not (Test-Path -LiteralPath $TrxDirectory -PathType Container)) {
        throw "The TRX directory is missing."
    }

    $matches = @(Get-ChildItem -LiteralPath $TrxDirectory -Filter "*.trx" -File -Recurse)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one TRX result, but found $($matches.Count)."
    }

    $trxFile = $matches[0]
    if (-not [string]::IsNullOrWhiteSpace($ExpectedTrxFileName)) {
        if ([System.IO.Path]::GetFileName($ExpectedTrxFileName) -cne $ExpectedTrxFileName -or $trxFile.Name -cne $ExpectedTrxFileName) {
            throw "The TRX result file name is not the expected fresh result name."
        }
    }

    if ($trxFile.Length -eq 0) {
        throw "The TRX result is empty."
    }

    if ($trxFile.LastWriteTimeUtc -lt $NotBeforeUtc.UtcDateTime) {
        throw "The TRX result predates the recorded test command and is stale."
    }

    try {
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $stringReader = [System.IO.StringReader]::new([System.IO.File]::ReadAllText($trxFile.FullName))
        $reader = [System.Xml.XmlReader]::Create($stringReader, $settings)
        try {
            $trx = [System.Xml.XmlDocument]::new()
            $trx.XmlResolver = $null
            $trx.Load($reader)
        }
        finally {
            $reader.Dispose()
            $stringReader.Dispose()
        }
    }
    catch {
        throw "The TRX result is malformed XML."
    }

    if ($null -eq $trx.DocumentElement -or $trx.DocumentElement.LocalName -ne "TestRun" -or [string]::IsNullOrWhiteSpace($trx.DocumentElement.NamespaceURI)) {
        throw "The TRX result does not contain a valid TestRun document."
    }

    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $namespaceManager.AddNamespace("trx", $trx.DocumentElement.NamespaceURI)
    $counters = $trx.SelectSingleNode("/trx:TestRun/trx:ResultSummary/trx:Counters", $namespaceManager)
    if ($counters -isnot [System.Xml.XmlElement]) {
        throw "The TRX result does not contain summary counters."
    }

    $total = Get-RequiredTrxCounter -Counters $counters -Name "total"
    $executed = Get-RequiredTrxCounter -Counters $counters -Name "executed"
    $passed = Get-RequiredTrxCounter -Counters $counters -Name "passed"
    $failed = Get-RequiredTrxCounter -Counters $counters -Name "failed"
    $notExecuted = Get-RequiredTrxCounter -Counters $counters -Name "notExecuted"
    if ($total -eq 0 -or $executed -ne $total -or $passed -ne $total -or $failed -ne 0 -or $notExecuted -ne 0) {
        throw "The TRX counters are not a non-empty fully passing result."
    }

    $testNames = [System.Collections.Generic.List[string]]::new()
    $executionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $resultNodes = $trx.SelectNodes("/trx:TestRun/trx:Results/trx:UnitTestResult", $namespaceManager)
    if ($null -eq $resultNodes -or $resultNodes.Count -ne $total) {
        throw "The TRX result count does not match its total counter."
    }

    foreach ($node in $resultNodes) {
        if ($node -isnot [System.Xml.XmlElement]) {
            throw "The TRX result contains an invalid test result node."
        }

        $name = $node.GetAttribute("testName")
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "The TRX result contains a test result without a test name."
        }

        if ($node.GetAttribute("outcome") -cne "Passed") {
            throw "The TRX result contains a failed or not-executed test result."
        }

        $executionId = $node.GetAttribute("executionId")
        $parsedExecutionId = [Guid]::Empty
        if ([string]::IsNullOrWhiteSpace($executionId) -or -not [Guid]::TryParse($executionId, [ref]$parsedExecutionId)) {
            throw "The TRX result contains a test result without a valid execution identity."
        }

        if (-not $executionIds.Add($executionId)) {
            throw "The TRX result contains duplicate execution identities."
        }

        $testNames.Add($name)
    }

    $notExecutedNodes = @($trx.SelectNodes("/trx:TestRun/trx:Results/trx:UnitTestResult[@outcome='NotExecuted']", $namespaceManager))
    if ($notExecutedNodes.Count -ne 0) {
        throw "The TRX result contains NotExecuted test result nodes."
    }

    $names = $testNames.ToArray()
    $sortedNames = Get-SortedOrdinalStrings -Values $names
    return [pscustomobject]@{
        file = $trxFile
        counters = [pscustomobject]@{
            total = $total
            executed = $executed
            passed = $passed
            failed = $failed
            notExecuted = $notExecuted
        }
        testNames = $sortedNames
        executionIds = @($executionIds | Sort-Object)
    }
}

function Test-OpenCoverNodeSkipped {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlElement]$Node
    )

    return $Node.GetAttribute("skipped") -ieq "true" -or -not [string]::IsNullOrWhiteSpace($Node.GetAttribute("skippedDueTo"))
}

function Test-OpenCoverMissingGeneratedReleaseSourcePath {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath,
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [bool]$SourceWasFileUri
    )

    if ($SourceWasFileUri -or
        (-not $RelativePath.EndsWith(".g.cs", [System.StringComparison]::OrdinalIgnoreCase) -and
         -not $RelativePath.EndsWith(".generated.cs", [System.StringComparison]::OrdinalIgnoreCase))) {
        return $false
    }

    $segments = @($RelativePath -split '[\\/]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $objIndex = -1
    for ($index = 1; $index -le ($segments.Count - 4); $index++) {
        if ($segments[$index] -ieq "obj" -and
            $segments[$index + 1] -ieq "Release" -and
            $segments[$index + 2] -cmatch '^net(?:coreapp|standard)?\d+(?:\.\d+)?(?:-[A-Za-z0-9.-]+)?$') {
            if ($objIndex -ne -1) {
                return $false
            }

            $objIndex = $index
        }
    }

    if ($objIndex -eq -1) {
        return $false
    }

    $projectDirectory = $RepositoryRoot
    for ($index = 0; $index -lt $objIndex; $index++) {
        $projectDirectory = Join-Path $projectDirectory $segments[$index]
    }

    return (Test-Path -LiteralPath $projectDirectory -PathType Container) -and
        @((Get-ChildItem -LiteralPath $projectDirectory -Filter "*.csproj" -File)).Count -gt 0
}

function Get-ValidatedOpenCoverSourcePath {
    param(
        [Parameter(Mandatory)]
        [string]$SourcePath,
        [string]$RepositoryRoot = ""
    )

    $localPath = $SourcePath
    $sourceWasFileUri = $false
    if (-not [System.IO.Path]::IsPathRooted($SourcePath)) {
        $uri = $null
        if (-not [System.Uri]::TryCreate($SourcePath, [System.UriKind]::Absolute, [ref]$uri)) {
            throw "The OpenCover report contains a source path that is not a local rooted path."
        }

        if (-not $uri.IsFile) {
            throw "The OpenCover report contains a source path with a non-file URI scheme."
        }

        $localPath = $uri.LocalPath
        $sourceWasFileUri = $true
    }

    if (-not [System.IO.Path]::IsPathRooted($localPath)) {
        throw "The OpenCover report contains a source path that is not a local rooted path."
    }

    $absolutePath = ConvertTo-AbsolutePath -Path $localPath
    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        return $absolutePath
    }

    $repositoryRootPath = ConvertTo-AbsolutePath -Path $RepositoryRoot
    $repositoryRootItem = Get-Item -LiteralPath $repositoryRootPath
    if (($repositoryRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne [System.IO.FileAttributes]::None) {
        throw "The OpenCover repository root cannot be a reparse point."
    }

    $rootWithSeparator = $repositoryRootPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $absolutePath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The OpenCover report contains a source path outside the verification repository root."
    }

    $relativePath = $absolutePath.Substring($rootWithSeparator.Length)
    $traversedPath = $repositoryRootPath
    foreach ($segment in ($relativePath -split '[\\/]+')) {
        $traversedPath = Join-Path $traversedPath $segment
        if (-not (Test-Path -LiteralPath $traversedPath)) {
            break
        }

        $item = Get-Item -LiteralPath $traversedPath
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne [System.IO.FileAttributes]::None) {
            throw "The OpenCover report contains a source path that traverses a reparse point."
        }
    }

    if (Test-Path -LiteralPath $absolutePath -PathType Leaf) {
        return $absolutePath
    }

    if (Test-OpenCoverMissingGeneratedReleaseSourcePath -RelativePath $relativePath -RepositoryRoot $repositoryRootPath -SourceWasFileUri $sourceWasFileUri) {
        return $null
    }

    throw "The OpenCover report contains a source path that cannot be resolved."

}

function Get-ValidatedOpenCoverReport {
    param(
        [Parameter(Mandatory)]
        [string]$CoveragePath,
        [Parameter(Mandatory)]
        [System.DateTimeOffset]$NotBeforeUtc,
        [string]$RepositoryRoot = ""
    )

    if (-not (Test-Path -LiteralPath $CoveragePath -PathType Leaf)) {
        throw "The OpenCover report is missing."
    }

    $coverageFile = Get-Item -LiteralPath $CoveragePath
    if ($coverageFile.Name -cne "coverage.opencover.xml") {
        throw "The OpenCover report file name is not the expected fresh report name."
    }

    if ($coverageFile.Length -eq 0) {
        throw "The OpenCover report is empty."
    }

    if ($coverageFile.LastWriteTimeUtc -lt $NotBeforeUtc.UtcDateTime) {
        throw "The OpenCover report predates the recorded test command and is stale."
    }

    if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot) -and -not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) {
        throw "The OpenCover repository root is missing."
    }

    try {
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $stringReader = [System.IO.StringReader]::new([System.IO.File]::ReadAllText($coverageFile.FullName))
        $reader = [System.Xml.XmlReader]::Create($stringReader, $settings)
        try {
            $coverage = [System.Xml.XmlDocument]::new()
            $coverage.XmlResolver = $null
            $coverage.Load($reader)
        }
        finally {
            $reader.Dispose()
            $stringReader.Dispose()
        }
    }
    catch {
        throw "The OpenCover report is malformed XML."
    }

    if ($null -eq $coverage.DocumentElement -or $coverage.DocumentElement.LocalName -ne "CoverageSession") {
        throw "The OpenCover report does not contain a valid CoverageSession document."
    }

    $moduleNodes = @($coverage.SelectNodes("/*[local-name()='CoverageSession']/*[local-name()='Modules']/*[local-name()='Module']"))
    $sourcePaths = [System.Collections.Generic.List[string]]::new()
    $moduleCount = 0
    $fileCount = 0
    foreach ($moduleNode in $moduleNodes) {
        if ($moduleNode -isnot [System.Xml.XmlElement] -or (Test-OpenCoverNodeSkipped -Node $moduleNode)) {
            continue
        }

        $moduleCount++
        $fileNodes = @($moduleNode.SelectNodes("./*[local-name()='Files']/*[local-name()='File']"))
        foreach ($fileNode in $fileNodes) {
            if ($fileNode -isnot [System.Xml.XmlElement] -or (Test-OpenCoverNodeSkipped -Node $fileNode)) {
                continue
            }

            $sourcePath = $fileNode.GetAttribute("fullPath")
            if ([string]::IsNullOrWhiteSpace($sourcePath)) {
                throw "The OpenCover report contains a file without a source path."
            }

            $validatedSourcePath = Get-ValidatedOpenCoverSourcePath -SourcePath $sourcePath -RepositoryRoot $RepositoryRoot
            if ($null -ne $validatedSourcePath) {
                $sourcePaths.Add($validatedSourcePath)
            }
            $fileCount++
        }
    }

    if ($moduleCount -eq 0 -or $fileCount -eq 0) {
        throw "The OpenCover report does not contain a non-empty non-skipped module and file set."
    }

    return [pscustomobject]@{
        file = $coverageFile
        moduleCount = $moduleCount
        fileCount = $fileCount
        localPathMode = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { "rooted" } else { "repository-rooted" }
        sourcePaths = Get-SortedOrdinalStrings -Values $sourcePaths.ToArray()
    }
}

function ConvertTo-CanonicalVSTestDisplayName {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $legacyEncodings = @(
        [Console]::OutputEncoding,
        [System.Text.Encoding]::GetEncoding(852, [System.Text.EncoderFallback]::ExceptionFallback, [System.Text.DecoderFallback]::ExceptionFallback),
        [System.Text.Encoding]::GetEncoding(1250, [System.Text.EncoderFallback]::ExceptionFallback, [System.Text.DecoderFallback]::ExceptionFallback)
    )
    foreach ($legacyEncoding in $legacyEncodings) {
        try {
            $candidate = $strictUtf8.GetString($legacyEncoding.GetBytes($Name))
            if ($candidate -cne $Name -and $legacyEncoding.GetString($strictUtf8.GetBytes($candidate)) -ceq $Name) {
                return $candidate
            }
        }
        catch {
            continue
        }
    }

    return $Name
}

function Get-ListedTestNames {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $names = [System.Collections.Generic.List[string]]::new()
    $insideListing = $false
    $previousNonEmptyLine = $null
    foreach ($line in $Lines) {
        $match = [regex]::Match($line, '^ {4}(?<name>\S(?:.*\S)?)$')
        if (-not $insideListing) {
            if ($match.Success -and $null -ne $previousNonEmptyLine -and $previousNonEmptyLine.TrimEnd().EndsWith(":")) {
                $insideListing = $true
                $names.Add((ConvertTo-CanonicalVSTestDisplayName -Name $match.Groups["name"].Value))
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($line) -and -not $match.Success) {
                $previousNonEmptyLine = $line
            }

            continue
        }

        if ($match.Success) {
            $names.Add((ConvertTo-CanonicalVSTestDisplayName -Name $match.Groups["name"].Value))
        }
        else {
            break
        }
    }

    if ($names.Count -eq 0) {
        throw "The --list-tests command did not produce a VSTest listing."
    }

    return ,(Get-SortedOrdinalStrings -Values $names.ToArray())
}

function New-FreshEvidenceDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $fullPath = ConvertTo-AbsolutePath -Path $Path
    if (Test-Path -LiteralPath $fullPath) {
        throw "The evidence directory already exists; refusing to overwrite prior evidence."
    }

    $null = New-Item -ItemType Directory -Path $fullPath -Force
    return $fullPath
}

Export-ModuleMember -Function @(
    "Assert-CommandMatchesDiscovery",
    "Assert-CleanRepositorySnapshot",
    "ConvertTo-AbsolutePath",
    "Get-DiscoveryManifest",
    "Get-FileSha256",
    "Get-ListedTestNames",
    "Get-ReleaseBuildManifest",
    "Get-RepositorySnapshot",
    "Get-SortedOrdinalStrings",
    "Get-StringListSha256",
    "Get-ValidatedTrx",
    "Get-ValidatedOpenCoverReport",
    "New-FreshEvidenceDirectory",
    "Protect-EvidenceString",
    "Protect-EvidenceValue",
    "Test-StringArraysEqualOrdinal",
    "Write-RedactedJsonFile"
)
