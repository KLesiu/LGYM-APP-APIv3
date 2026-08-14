param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('HarnessOnly')]
    [string] $Mode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$HarnessDockerReceiptVariable = 'HARNESS_ONLY_HARNESS_DOCKER_RECEIPT_PATH'
$LifecycleReceiptVariable = 'HARNESS_ONLY_LIFECYCLE_RECEIPT_PATH'
$HarnessDockerContracts = @(
    'PostgreSQL_container_starts_with_module_readiness_and_is_removed_on_disposal',
    'PostgreSQL_container_is_removed_when_a_test_local_failure_occurs_after_start',
    'PostgreSQL_post_container_start_callback_failure_proves_private_locator_absence',
    'PostgreSQL_sequential_leases_have_distinct_redacted_observations_and_are_absent'
)
$LifecycleContracts = @(
    'Lifecycle_hooks_are_async_tag_scoped_and_explicitly_ordered',
    'Lifecycle_feature_declares_exactly_two_canonical_serial_probes',
    'Scenario_failure_after_the_ready_stack_preserves_the_primary_failure_writes_one_safe_artifact_and_starts_fresh',
    'Scenario_success_writes_no_failure_artifact_and_removes_the_completed_run',
    'Compiled_test_inventory_requires_nonempty_disjoint_serial_categories_without_parallel_markers'
)
$CaseIds = @('lifecycle-probe-a', 'lifecycle-probe-b')
$AcquisitionCategories = @('scenario-paths', 'postgresql', 'external-api-host', 'expo', 'browser-run', 'browser-scenario')
$CleanupCategories = @('browser-scenario', 'browser-run', 'expo', 'external-api-host', 'scenario-paths')

function Fail {
    throw [System.InvalidOperationException]::new('HarnessOnly coordinator validation failed.')
}

function Assert-Condition {
    param([bool] $Condition)

    if (-not $Condition) {
        Fail
    }
}

function Get-AbsoluteApplication {
    param([string] $Name)

    $command = @(Get-Command -Name $Name, "$Name.exe", "$Name.cmd" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)[0]
    Assert-Condition ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Path))
    $path = [System.IO.Path]::GetFullPath($command.Path)
    Assert-Condition ([System.IO.File]::Exists($path))
    return $path
}

function Assert-ContainedPath {
    param(
        [string] $Parent,
        [string] $Candidate,
        [bool] $AllowSame = $false
    )

    $parentPath = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Parent))
    $candidatePath = [System.IO.Path]::GetFullPath($Candidate)
    $relative = [System.IO.Path]::GetRelativePath($parentPath, $candidatePath)
    $isDescendant = -not [System.IO.Path]::IsPathRooted($relative) -and
        $relative -ne '..' -and
        -not $relative.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::Ordinal)
    Assert-Condition ($isDescendant -and ($AllowSame -or $relative -ne '.'))
    return $candidatePath
}

function Assert-NoReparsePoints {
    param(
        [string] $Root,
        [string] $Target
    )

    $rootPath = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Root))
    $targetPath = Assert-ContainedPath -Parent $rootPath -Candidate $Target -AllowSame $true
    if (([System.IO.Directory]::Exists($rootPath) -or [System.IO.File]::Exists($rootPath)) -and
        (([System.IO.File]::GetAttributes($rootPath) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
        Fail
    }
    $relative = [System.IO.Path]::GetRelativePath($rootPath, $targetPath)
    $current = $rootPath
    foreach ($segment in $relative.Split([System.IO.Path]::DirectorySeparatorChar, [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (([System.IO.Directory]::Exists($current) -or [System.IO.File]::Exists($current)) -and
            (([System.IO.File]::GetAttributes($current) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            Fail
        }
    }
}

function Get-RequiredFile {
    param([string] $Path)

    Assert-Condition ([System.IO.File]::Exists($Path))
    Assert-NoReparsePoints -Root $RepositoryRoot -Target $Path
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-JsonDocument {
    param([string] $Path)

    try {
        return [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($Path))
    }
    catch {
        Fail
    }
}

function Assert-ExactProperties {
    param(
        [System.Text.Json.JsonElement] $Element,
        [string[]] $Expected
    )

    Assert-Condition ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object)
    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) {
        Assert-Condition ($names.Add($property.Name) -and [System.Linq.Enumerable]::Contains([string[]] $Expected, $property.Name, [System.StringComparer]::Ordinal))
    }
    Assert-Condition ($names.Count -eq $Expected.Count)
}

function Get-JsonString {
    param([System.Text.Json.JsonElement] $Element, [string] $Name)

    $value = $Element.GetProperty($Name)
    Assert-Condition ($value.ValueKind -eq [System.Text.Json.JsonValueKind]::String)
    $text = $value.GetString()
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($text))
    return $text
}

function Get-JsonNonNegativeInt {
    param([System.Text.Json.JsonElement] $Element, [string] $Name)

    $value = $Element.GetProperty($Name)
    $number = 0
    Assert-Condition ($value.ValueKind -eq [System.Text.Json.JsonValueKind]::Number -and $value.TryGetInt32([ref] $number) -and $number -ge 0)
    return $number
}

function Assert-JsonTrue {
    param([System.Text.Json.JsonElement] $Element, [string] $Name)

    Assert-Condition ($Element.GetProperty($Name).ValueKind -eq [System.Text.Json.JsonValueKind]::True)
}

function Get-ExactJsonNames {
    param(
        [System.Text.Json.JsonElement] $Element,
        [string] $Name,
        [string[]] $Expected
    )

    $items = $Element.GetProperty($Name)
    Assert-Condition ($items.ValueKind -eq [System.Text.Json.JsonValueKind]::Array)
    $values = @($items.EnumerateArray() | ForEach-Object {
        Assert-Condition ($_.ValueKind -eq [System.Text.Json.JsonValueKind]::String)
        $_.GetString()
    })
    Assert-Condition ($values.Count -eq $Expected.Count)
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert-Condition ($values[$index] -ceq $Expected[$index])
    }
    return $values
}

function Read-Counter {
    param([System.Xml.XmlElement] $Counters, [string] $Name)

    $value = $Counters.GetAttribute($Name)
    $number = 0
    Assert-Condition ([int]::TryParse($value, [System.Globalization.NumberStyles]::None, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $number) -and $number -ge 0)
    return $number
}

function Parse-Trx {
    param([string] $Path, [string[]] $RequiredContracts)

    try {
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $document = [System.Xml.XmlDocument]::new()
        $reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new([System.IO.File]::ReadAllText($Path)), $settings)
        try {
            $document.Load($reader)
        }
        finally {
            $reader.Dispose()
        }

        $counterNodes = @($document.SelectNodes("//*[local-name()='Counters']"))
        Assert-Condition ($counterNodes.Count -eq 1)
        $counters = [System.Xml.XmlElement] $counterNodes[0]
        $resultNodes = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
        $results = @($resultNodes | ForEach-Object {
            [pscustomobject]@{ Name = $_.GetAttribute('testName'); Outcome = $_.GetAttribute('outcome') }
        })
        $projection = [ordered]@{
            total = Read-Counter $counters 'total'
            executed = Read-Counter $counters 'executed'
            passed = Read-Counter $counters 'passed'
            failed = Read-Counter $counters 'failed'
            timeout = Read-Counter $counters 'timeout'
            notExecuted = Read-Counter $counters 'notExecuted'
        }
        Assert-Condition ($projection.total -gt 0 -and $projection.executed -eq $projection.total -and $projection.passed -eq $projection.total -and $projection.failed -eq 0 -and $projection.timeout -eq 0 -and $projection.notExecuted -eq 0 -and $results.Count -eq $projection.total)
        Assert-Condition (-not ($results | Where-Object { [string]::IsNullOrWhiteSpace($_.Name) -or $_.Outcome -cne 'Passed' }))
        $names = @($results | ForEach-Object Name)
        $uniqueNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($name in $names) {
            Assert-Condition ($uniqueNames.Add($name))
        }
        Assert-Condition (-not ($names | Where-Object {
            $_ -match 'issue-433|issue-434|FinalWebHarnessEvidence|SanitizedApiHostEvidence|FinalTrxManifest|Pinned_source_is_exported_started_and_navigated_by_Chromium'
        }))
        foreach ($contract in $RequiredContracts) {
            Assert-Condition ((@($names | Where-Object { $_ -ceq $contract })).Count -eq 1)
        }
        return [pscustomobject] $projection
    }
    catch {
        Fail
    }
}

function Parse-DockerReceipt {
    param([string] $Path)

    $document = Get-JsonDocument $Path
    try {
        $root = $document.RootElement
        Assert-ExactProperties $root @('testCount', 'passedCount', 'allContainersAbsent', 'identitiesDistinct', 'rawIdentitiesExcluded')
        $testCount = Get-JsonNonNegativeInt $root 'testCount'
        $passedCount = Get-JsonNonNegativeInt $root 'passedCount'
        Assert-JsonTrue $root 'allContainersAbsent'
        Assert-JsonTrue $root 'identitiesDistinct'
        Assert-JsonTrue $root 'rawIdentitiesExcluded'
        Assert-Condition ($testCount -gt 0 -and $passedCount -eq $testCount)
        return [ordered]@{ testCount = $testCount; passedCount = $passedCount; allContainersAbsent = $true; identitiesDistinct = $true; rawIdentitiesExcluded = $true }
    }
    finally {
        $document.Dispose()
    }
}

function Parse-LifecycleReceipt {
    param([string] $Path, [string] $ApiHeadSha, [bool] $RepositoryDirty)

    $document = Get-JsonDocument $Path
    try {
        $root = $document.RootElement
        Assert-ExactProperties $root @('schema', 'apiHeadSha', 'apiRepositoryDirty', 'completedScenarioCount', 'sourceStatePreserved', 'runtimeRootAbsent', 'successArtifactsAbsent', 'scenarios')
        Assert-Condition ((Get-JsonString $root 'schema') -ceq 'issue-435-lifecycle-run-receipt-v1')
        Assert-Condition ((Get-JsonString $root 'apiHeadSha') -ceq $ApiHeadSha)
        $dirtyValue = $root.GetProperty('apiRepositoryDirty').ValueKind
        Assert-Condition (($dirtyValue -eq [System.Text.Json.JsonValueKind]::True) -or ($dirtyValue -eq [System.Text.Json.JsonValueKind]::False))
        Assert-Condition (($dirtyValue -eq [System.Text.Json.JsonValueKind]::True) -eq $RepositoryDirty)
        Assert-Condition ((Get-JsonNonNegativeInt $root 'completedScenarioCount') -eq 2)
        Assert-JsonTrue $root 'sourceStatePreserved'
        Assert-JsonTrue $root 'runtimeRootAbsent'
        Assert-JsonTrue $root 'successArtifactsAbsent'
        $scenarios = $root.GetProperty('scenarios')
        Assert-Condition ($scenarios.ValueKind -eq [System.Text.Json.JsonValueKind]::Array -and $scenarios.GetArrayLength() -eq 2)
        $safeScenarios = @()
        foreach ($scenario in $scenarios.EnumerateArray()) {
            Assert-ExactProperties $scenario @('caseId', 'acquiredCategories', 'cleanupCategories', 'cleanupFailureCount', 'freshPostgreSql', 'freshApiHost', 'freshExpo', 'freshBrowserRun', 'freshBrowserScenario', 'previousResourcesAbsent', 'browserStorageEmpty', 'databaseAbsent', 'apiAbsent', 'expoAbsent', 'scenarioPathsAbsent')
            $caseId = Get-JsonString $scenario 'caseId'
            Assert-Condition ([System.Linq.Enumerable]::Contains([string[]] $CaseIds, $caseId, [System.StringComparer]::Ordinal))
            $null = Get-ExactJsonNames $scenario 'acquiredCategories' $AcquisitionCategories
            $null = Get-ExactJsonNames $scenario 'cleanupCategories' $CleanupCategories
            Assert-Condition ((Get-JsonNonNegativeInt $scenario 'cleanupFailureCount') -eq 0)
            foreach ($fact in @('freshPostgreSql', 'freshApiHost', 'freshExpo', 'freshBrowserRun', 'freshBrowserScenario', 'previousResourcesAbsent', 'browserStorageEmpty', 'databaseAbsent', 'apiAbsent', 'expoAbsent', 'scenarioPathsAbsent')) {
                Assert-JsonTrue $scenario $fact
            }
            $safeScenarios += [ordered]@{ caseId = $caseId; acquiredCategories = $AcquisitionCategories; cleanupCategories = $CleanupCategories; cleanupFailureCount = 0; freshPostgreSql = $true; freshApiHost = $true; freshExpo = $true; freshBrowserRun = $true; freshBrowserScenario = $true; previousResourcesAbsent = $true; browserStorageEmpty = $true; databaseAbsent = $true; apiAbsent = $true; expoAbsent = $true; scenarioPathsAbsent = $true }
        }
        Assert-Condition ((@($safeScenarios | ForEach-Object caseId | Sort-Object) -join ',') -ceq (($CaseIds | Sort-Object) -join ','))
        return [ordered]@{ completedScenarioCount = 2; sourceStatePreserved = $true; runtimeRootAbsent = $true; successArtifactsAbsent = $true; scenarios = @($safeScenarios | Sort-Object caseId) }
    }
    finally {
        $document.Dispose()
    }
}

function Invoke-BoundedProcess {
    param(
        [string] $FileName,
        [string[]] $Arguments,
        [timespan] $ExecutionTimeout,
        [hashtable] $Environment = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($FileName)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $null = $startInfo.ArgumentList.Add($argument)
    }
    if ($Environment.Count -gt 0) {
        $startInfo.Environment.Clear()
        foreach ($entry in $Environment.GetEnumerator()) {
            $startInfo.Environment[$entry.Key] = $entry.Value
        }
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    Assert-Condition ($null -ne $process)
    try {
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $deadline = [System.Threading.CancellationTokenSource]::new($ExecutionTimeout)
        try {
            $null = $process.WaitForExitAsync($deadline.Token).GetAwaiter().GetResult()
            $null = [System.Threading.Tasks.Task]::WhenAll($standardOutput, $standardError).WaitAsync($deadline.Token).GetAwaiter().GetResult()
        }
        catch [System.OperationCanceledException] {
            if (-not $process.HasExited) {
                $process.Kill($true)
                $shutdown = [System.Threading.CancellationTokenSource]::new([timespan]::FromSeconds(5))
                try {
                    $null = $process.WaitForExitAsync($shutdown.Token).GetAwaiter().GetResult()
                }
                catch [System.OperationCanceledException] {
                }
                finally {
                    $shutdown.Dispose()
                }
            }

            Fail
        }
        finally {
            $deadline.Dispose()
        }

        $output = $standardOutput.GetAwaiter().GetResult()
        Assert-Condition ($output.Length -le 4096)
        Assert-Condition ($process.ExitCode -eq 0)
        return $output
    }
    finally {
        $process.Dispose()
    }
}

function Get-RepositoryState {
    param([string] $GitPath, [timespan] $ExecutionTimeout)

    $head = (Invoke-BoundedProcess $GitPath @('-C', $RepositoryRoot, '--no-optional-locks', 'rev-parse', 'HEAD') $ExecutionTimeout).Trim()
    Assert-Condition ($head -match '^[0-9a-f]{40}$')
    $status = Invoke-BoundedProcess $GitPath @('-C', $RepositoryRoot, '--no-optional-locks', 'status', '--porcelain=v1', '--untracked-files=all') $ExecutionTimeout
    return [pscustomobject]@{ HeadSha = $head; RepositoryDirty = -not [string]::IsNullOrWhiteSpace($status) }
}

function Assert-FreshFile {
    param([string] $Path, [datetime] $StartedAtUtc)

    Assert-Condition ([System.IO.File]::Exists($Path))
    Assert-Condition (([System.IO.File]::GetLastWriteTimeUtc($Path)) -ge $StartedAtUtc)
}

function Invoke-ChildTest {
    param(
        [string] $DotNetPath,
        [string] $ProjectPath,
        [string] $RunSettingsPath,
        [string] $ResultsDirectory,
        [string] $Category,
        [string] $TrxFileName,
        [hashtable] $ChildEnvironment,
        [timespan] $ExecutionTimeout
    )

    $arguments = @('test', $ProjectPath, '--configuration', 'Release', '--no-build', '--settings', $RunSettingsPath, '--filter', "TestCategory=$Category", '--results-directory', $ResultsDirectory, '--logger', "trx;LogFileName=$TrxFileName")
    $null = Invoke-BoundedProcess $DotNetPath $arguments $ExecutionTimeout $ChildEnvironment
}

function Restore-EnvironmentVariable {
    param([string] $Name, [AllowNull()][string] $Value)

    if ($null -eq $Value) {
        Remove-Item -LiteralPath "Env:$Name" -ErrorAction SilentlyContinue
    }
    else {
        Set-Item -LiteralPath "Env:$Name" -Value $Value
    }
}

function Invoke-HarnessOnly {
    $scriptProjectRoot = Split-Path -Parent $PSScriptRoot
    $script:RepositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $scriptProjectRoot))
    $projectPath = Get-RequiredFile (Join-Path $RepositoryRoot 'LgymApi.E2ETests/LgymApi.E2ETests.csproj')
    $runSettingsPath = Get-RequiredFile (Join-Path $RepositoryRoot 'LgymApi.E2ETests/LgymApi.E2ETests.runsettings')
    $configPath = Get-RequiredFile (Join-Path $RepositoryRoot 'LgymApi.E2ETests/appsettings.E2E.json')
    $e2eAssemblyPath = Get-RequiredFile (Join-Path $RepositoryRoot 'LgymApi.E2ETests/bin/Release/net10.0/LgymApi.E2ETests.dll')
    $dotNetPath = Get-AbsoluteApplication 'dotnet'
    $gitPath = Get-AbsoluteApplication 'git'
    $nodePath = Get-AbsoluteApplication 'node'
    $npmPath = Get-AbsoluteApplication 'npm'
    $dockerPath = Get-AbsoluteApplication 'docker'
    $config = Get-JsonDocument $configPath
    try {
        $root = $config.RootElement
        $publishedRelativePath = Get-JsonString $root.GetProperty('E2E').GetProperty('Api') 'PublishedDllPath'
        Assert-Condition (-not [System.IO.Path]::IsPathRooted($publishedRelativePath))
        $publishedDllPath = Assert-ContainedPath -Parent $RepositoryRoot -Candidate (Join-Path $RepositoryRoot $publishedRelativePath)
        $publishedDirectory = Split-Path -Parent $publishedDllPath
        foreach ($artifact in @($publishedDllPath, (Join-Path $publishedDirectory 'LgymApi.Api.deps.json'), (Join-Path $publishedDirectory 'LgymApi.Api.runtimeconfig.json'))) {
            $null = Get-RequiredFile $artifact
        }
        $hash = (Get-FileHash -LiteralPath $publishedDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Condition ($hash -match '^[0-9a-f]{64}$')
        $testSessionSeconds = Get-JsonNonNegativeInt $root.GetProperty('E2E').GetProperty('Timeouts') 'TestSessionSeconds'
        Assert-Condition ($testSessionSeconds -gt 0)
        $sourcePath = [Environment]::GetEnvironmentVariable('LGYM_E2E__WebSource__SourcePath')
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($sourcePath) -and [System.IO.Path]::IsPathFullyQualified($sourcePath) -and [System.IO.Directory]::Exists($sourcePath))
        $preflightTimeout = [timespan]::FromSeconds($testSessionSeconds)
        $sourceHead = (Invoke-BoundedProcess $gitPath @('-C', $sourcePath, '--no-optional-locks', 'rev-parse', 'HEAD') $preflightTimeout).Trim()
        Assert-Condition ($sourceHead -ceq (Get-JsonString $root.GetProperty('E2E').GetProperty('WebSource') 'CommitSha'))
        $port = Get-JsonNonNegativeInt $root.GetProperty('E2E').GetProperty('Web') 'Port'
        Assert-Condition ($port -eq 8083 -and -not ([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $port))
    }
    finally {
        $config.Dispose()
    }
    $nodeVersion = Invoke-BoundedProcess $nodePath @('--version') $preflightTimeout
    Assert-Condition ($nodeVersion -match '^v?(2[2-9]|[3-9][0-9])\.')
    $null = Invoke-BoundedProcess $npmPath @('--version') $preflightTimeout
    $null = Invoke-BoundedProcess $dockerPath @('info', '--format', '{{.ServerVersion}}') $preflightTimeout
    $browserRoot = Join-Path $RepositoryRoot '.e2e-private/browsers'
    Assert-NoReparsePoints -Root $RepositoryRoot -Target $browserRoot
    Assert-Condition ([System.IO.Directory]::Exists($browserRoot) -and $null -ne (Get-ChildItem -LiteralPath $browserRoot -Filter 'chrome.exe' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1))

    $resultsDirectory = Join-Path $RepositoryRoot 'LgymApi.E2ETests/TestResults/issue-435-harness-only'
    Assert-NoReparsePoints -Root $RepositoryRoot -Target $resultsDirectory
    [System.IO.Directory]::CreateDirectory($resultsDirectory) | Out-Null
    Assert-NoReparsePoints -Root $RepositoryRoot -Target $resultsDirectory
    $outputs = [ordered]@{
        HarnessDockerTrx = Join-Path $resultsDirectory 'issue-435-harness-docker.trx'
        LifecycleTrx = Join-Path $resultsDirectory 'issue-435-lifecycle.trx'
        HarnessDockerReceipt = Join-Path $resultsDirectory 'issue-435-harness-docker.receipt.json'
        LifecycleReceipt = Join-Path $resultsDirectory 'issue-435-lifecycle.receipt.json'
        Manifest = Join-Path $resultsDirectory 'issue-435-lifecycle-evidence.json'
    }
    foreach ($output in $outputs.Values) {
        Assert-ContainedPath -Parent $resultsDirectory -Candidate $output | Out-Null
        if ([System.IO.File]::Exists($output)) {
            Remove-Item -LiteralPath $output -Force
        }
    }

    $originalHarnessReceipt = [Environment]::GetEnvironmentVariable($HarnessDockerReceiptVariable)
    $originalLifecycleReceipt = [Environment]::GetEnvironmentVariable($LifecycleReceiptVariable)
    $systemRoot = [Environment]::GetEnvironmentVariable('SystemRoot')
    $windowsDirectory = [Environment]::GetEnvironmentVariable('WINDIR')
    $path = [Environment]::GetEnvironmentVariable('PATH')
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($systemRoot) -and -not [string]::IsNullOrWhiteSpace($windowsDirectory) -and -not [string]::IsNullOrWhiteSpace($path))
    $childEnvironment = @{
        'SystemRoot' = $systemRoot
        'WINDIR' = $windowsDirectory
        'TEMP' = [System.IO.Path]::GetTempPath()
        'TMP' = [System.IO.Path]::GetTempPath()
        'PATH' = $path
        'LGYM_E2E__WebSource__SourcePath' = $sourcePath
        $HarnessDockerReceiptVariable = $outputs.HarnessDockerReceipt
        $LifecycleReceiptVariable = $outputs.LifecycleReceipt
    }
    foreach ($name in @('DOCKER_HOST', 'DOCKER_CONTEXT', 'DOCKER_CONFIG', 'DOCKER_TLS_VERIFY', 'DOCKER_CERT_PATH', 'TESTCONTAINERS_HOST_OVERRIDE', 'TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $childEnvironment[$name] = $value
        }
    }
    foreach ($name in @('TESTCONTAINERS_RYUK_DISABLED', 'TESTCONTAINERS_REUSE_ENABLE')) {
        Assert-Condition ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name)))
    }
    $childExecutionTimeout = [timespan]::FromSeconds($testSessionSeconds)
    $startedAtUtc = [datetime]::UtcNow.AddSeconds(-1)
    try {
        Invoke-ChildTest $dotNetPath $projectPath $runSettingsPath $resultsDirectory 'HarnessDocker' 'issue-435-harness-docker.trx' $childEnvironment $childExecutionTimeout
        Invoke-ChildTest $dotNetPath $projectPath $runSettingsPath $resultsDirectory 'Lifecycle' 'issue-435-lifecycle.trx' $childEnvironment $childExecutionTimeout
    }
    finally {
        Restore-EnvironmentVariable $HarnessDockerReceiptVariable $originalHarnessReceipt
        Restore-EnvironmentVariable $LifecycleReceiptVariable $originalLifecycleReceipt
    }

    foreach ($path in @($outputs.HarnessDockerTrx, $outputs.LifecycleTrx, $outputs.HarnessDockerReceipt, $outputs.LifecycleReceipt)) {
        Assert-FreshFile $path $startedAtUtc
    }
    $repositoryState = Get-RepositoryState $gitPath $preflightTimeout
    $harnessDocker = Parse-Trx $outputs.HarnessDockerTrx $HarnessDockerContracts
    $lifecycle = Parse-Trx $outputs.LifecycleTrx $LifecycleContracts
    $dockerReceipt = Parse-DockerReceipt $outputs.HarnessDockerReceipt
    $lifecycleReceipt = Parse-LifecycleReceipt $outputs.LifecycleReceipt $repositoryState.HeadSha $repositoryState.RepositoryDirty
    $manifest = [ordered]@{
        schema = 'issue-435-lifecycle-evidence-v1'
        api = [ordered]@{ headSha = $repositoryState.HeadSha; repositoryDirty = $repositoryState.RepositoryDirty }
        harnessDocker = [ordered]@{ counters = $harnessDocker; contractNames = @($HarnessDockerContracts | Sort-Object); receipt = $dockerReceipt }
        lifecycle = [ordered]@{ counters = $lifecycle; contractNames = @($LifecycleContracts | Sort-Object); run = $lifecycleReceipt }
    }
    [System.IO.File]::WriteAllText($outputs.Manifest, ($manifest | ConvertTo-Json -Depth 12))
}

try {
    Invoke-HarnessOnly
    [Console]::Out.WriteLine('HarnessOnly coordinator completed.')
    exit 0
}
catch {
    [Console]::Error.WriteLine('HarnessOnly coordinator failed.')
    exit 1
}
