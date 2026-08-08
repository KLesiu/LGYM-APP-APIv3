param(
    [string]$ConnectionString = "",
    [string]$ResultsDirectory = "TestResults/PostgreSql",
    [ValidateRange(1, 300)]
    [int]$StartupTimeoutSeconds = 60,
    [switch]$NoBuild,
    [string]$TestFilter = "",
    [string]$CoverageOutput = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Protect-Message {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Message,
        [string[]]$SensitiveValues = @()
    )

    $protectedMessage = $Message
    foreach ($sensitiveValue in $SensitiveValues) {
        if (-not [string]::IsNullOrWhiteSpace($sensitiveValue)) {
            $protectedMessage = $protectedMessage.Replace($sensitiveValue, "[redacted]")
        }
    }

    return $protectedMessage -replace '(?i)\b(password|pwd)\s*=\s*[^;\s''"]+', '$1=[redacted]'
}

function Assert-DockerAvailable {
    if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker is required when -ConnectionString is not supplied. Install Docker and ensure its daemon is running, or supply an admin PostgreSQL connection string."
    }

    $null = & docker version --format "{{.Server.Version}}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker is unavailable when -ConnectionString is not supplied. Start the Docker daemon, or supply an admin PostgreSQL connection string."
    }
}

function Get-DockerMappedPort {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerName
    )

    $portOutput = & docker port $ContainerName "5432/tcp" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not determine the PostgreSQL Docker port."
    }

    $portMatch = [regex]::Match(($portOutput -join "`n"), ':(?<port>\d+)\s*$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $portMatch.Success) {
        throw "Docker did not publish a usable PostgreSQL port."
    }

    return [int]$portMatch.Groups["port"].Value
}

function Wait-ForDockerPostgreSql {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerName,
        [Parameter(Mandatory)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $null = & docker exec $ContainerName pg_isready -h "127.0.0.1" -U "postgres" -d "postgres" 2>$null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Timed out waiting for the PostgreSQL Docker container to become ready after $TimeoutSeconds seconds."
}

function Get-GeneratedTrx {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,
        [Parameter(Mandatory)]
        [string]$FileName
    )

    $generatedTrxFiles = @(Get-ChildItem -LiteralPath $Directory -Filter $FileName -File -Recurse)
    if ($generatedTrxFiles.Count -ne 1) {
        throw "Expected exactly one PostgreSQL TRX result named '$FileName' under '$Directory', but found $($generatedTrxFiles.Count)."
    }

    if ($generatedTrxFiles[0].Length -eq 0) {
        throw "The PostgreSQL TRX result '$($generatedTrxFiles[0].FullName)' is empty."
    }

    return $generatedTrxFiles[0]
}

function Get-RequiredTrxCounter {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlElement]$Counters,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = $Counters.GetAttribute($Name)
    $parsedValue = 0
    if ([string]::IsNullOrWhiteSpace($value) -or -not [int]::TryParse($value, [ref]$parsedValue)) {
        throw "The PostgreSQL TRX result does not contain a valid '$Name' counter."
    }

    return $parsedValue
}

function Assert-PassingTrx {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$TrxFile
    )

    try {
        [xml]$trx = Get-Content -LiteralPath $TrxFile.FullName -Raw
    }
    catch {
        throw "Could not parse PostgreSQL TRX result '$($TrxFile.FullName)'."
    }

    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $namespaceManager.AddNamespace("trx", $trx.DocumentElement.NamespaceURI)
    $counters = $trx.SelectSingleNode("/trx:TestRun/trx:ResultSummary/trx:Counters", $namespaceManager)
    if ($null -eq $counters -or $counters -isnot [System.Xml.XmlElement]) {
        throw "The PostgreSQL TRX result '$($TrxFile.FullName)' does not contain result counters."
    }

    $total = Get-RequiredTrxCounter -Counters $counters -Name "total"
    $executed = Get-RequiredTrxCounter -Counters $counters -Name "executed"
    $passed = Get-RequiredTrxCounter -Counters $counters -Name "passed"
    $failed = Get-RequiredTrxCounter -Counters $counters -Name "failed"
    $notExecuted = Get-RequiredTrxCounter -Counters $counters -Name "notExecuted"

    if ($total -eq 0 -or $executed -ne $total -or $passed -ne $total -or $failed -ne 0 -or $notExecuted -ne 0) {
        throw "PostgreSQL TRX counters are not a non-empty passing result: total=$total, executed=$executed, passed=$passed, failed=$failed, notExecuted=$notExecuted."
    }

    $notExecutedResults = @(
        $trx.SelectNodes("/trx:TestRun/trx:Results/trx:UnitTestResult", $namespaceManager) |
            Where-Object {
                $_ -is [System.Xml.XmlElement] -and $_.GetAttribute("outcome") -eq "NotExecuted"
            }
    )
    if ($notExecutedResults.Count -ne 0) {
        throw "PostgreSQL TRX contains $($notExecutedResults.Count) NotExecuted test result(s) despite its summary counters."
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot "LgymApi.IntegrationTests/LgymApi.IntegrationTests.csproj"
if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
    throw "Could not find the integration test project. Run this script from the repository checkout."
}

if (-not [System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repositoryRoot $ResultsDirectory
}

if (-not [string]::IsNullOrWhiteSpace($CoverageOutput)) {
    if (-not [System.IO.Path]::IsPathRooted($CoverageOutput)) {
        $CoverageOutput = Join-Path $repositoryRoot $CoverageOutput
    }

    $coverageDirectory = Split-Path -Parent $CoverageOutput
    $null = New-Item -ItemType Directory -Path $coverageDirectory -Force
}

$resultsPath = (New-Item -ItemType Directory -Path $ResultsDirectory -Force).FullName
$trxFileName = "integration-tests-with-postgresql-$([Guid]::NewGuid().ToString('N')).trx"
$originalPostgreSqlEnvironment = [Environment]::GetEnvironmentVariable("LGYM_TEST_POSTGRES", "Process")
$environmentWasSet = $false
$containerName = $null
$testExitCode = $null
$adminConnectionString = $null
$sensitiveValues = @($ConnectionString)
$exitCode = 0

try {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        Assert-DockerAvailable

        $containerName = "lgym-postgres-tests-$([Guid]::NewGuid().ToString('N'))"
        $dockerPassword = [Guid]::NewGuid().ToString('N')
        $sensitiveValues += $dockerPassword

        $null = & docker run --detach --name $containerName --publish "127.0.0.1::5432" --env "POSTGRES_USER=postgres" --env "POSTGRES_PASSWORD=$dockerPassword" --env "POSTGRES_DB=postgres" "postgres:17-alpine"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not start the PostgreSQL Docker container."
        }

        Wait-ForDockerPostgreSql -ContainerName $containerName -TimeoutSeconds $StartupTimeoutSeconds
        $port = Get-DockerMappedPort -ContainerName $containerName
        $adminConnectionString = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Password=$dockerPassword;Pooling=false;Timeout=5;Command Timeout=30"
        $sensitiveValues += $adminConnectionString
    }
    else {
        $adminConnectionString = $ConnectionString
    }

    [Environment]::SetEnvironmentVariable("LGYM_TEST_POSTGRES", $adminConnectionString, "Process")
    $environmentWasSet = $true

    $testArguments = @(
        "test",
        $testProject,
        "--configuration", "Release",
        "--logger", "trx;LogFileName=$trxFileName",
        "--results-directory", $resultsPath,
        "--verbosity", "normal"
    )
    if ($NoBuild) {
        $testArguments += "--no-build"
    }
    if (-not [string]::IsNullOrWhiteSpace($CoverageOutput)) {
        $testArguments += "/p:CollectCoverage=true", "/p:CoverletOutputFormat=opencover", "/p:CoverletOutput=$CoverageOutput"
    }
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $testArguments += "--filter", $TestFilter
        Write-Host "Running focused PostgreSQL test slice. This does not replace the unfiltered PostgreSQL suite."
    }

    $originalErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $testOutput = & dotnet @testArguments 2>&1
        $testExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $originalErrorActionPreference
    }
    foreach ($line in $testOutput) {
        Write-Host (Protect-Message -Message ([string]$line) -SensitiveValues $sensitiveValues)
    }

    $trxFile = Get-GeneratedTrx -Directory $resultsPath -FileName $trxFileName
    Assert-PassingTrx -TrxFile $trxFile

    if ($testExitCode -ne 0) {
        throw "The PostgreSQL integration test command exited with code $testExitCode despite a passing TRX result."
    }

    Write-Host "Integration tests with PostgreSQL passed. TRX: $($trxFile.FullName)"
}
catch {
    $failureMessage = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($failureMessage)) {
        $failureMessage = "PostgreSQL integration test runner failed without an error message."
    }

    Write-Error (Protect-Message -Message $failureMessage -SensitiveValues $sensitiveValues)
    $exitCode = 1
}
finally {
    if ($environmentWasSet) {
        [Environment]::SetEnvironmentVariable("LGYM_TEST_POSTGRES", $originalPostgreSqlEnvironment, "Process")
    }

    if ($null -ne $containerName) {
        $null = & docker rm --force --volumes $containerName 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Could not remove PostgreSQL Docker container '$containerName'."
            $exitCode = 1
        }
    }
}

exit $exitCode
