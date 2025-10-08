# PowerShell script to run EasyDispatch performance benchmarks
# Usage: .\run-benchmarks.ps1 [-Suite <suite-name>] [-Compare] [-Export]

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("All", "Handler", "Notification", "Streaming", "Behavior", "ColdStart", "Concurrent", "Comparison")]
    [string]$Suite = "All",
    
    [Parameter(Mandatory=$false)]
    [switch]$Compare,
    
    [Parameter(Mandatory=$false)]
    [switch]$Export,
    
    [Parameter(Mandatory=$false)]
    [string]$BaselinePath = "baseline"
)

$ErrorActionPreference = "Stop"

# Colors
$SuccessColor = "Green"
$ErrorColor = "Red"
$InfoColor = "Cyan"

function Write-Info {
    param([string]$Message)
    Write-Host $Message -ForegroundColor $InfoColor
}

function Write-Success {
    param([string]$Message)
    Write-Host $Message -ForegroundColor $SuccessColor
}

function Write-Error {
    param([string]$Message)
    Write-Host $Message -ForegroundColor $ErrorColor
}

# Check if we're in the right directory
if (-not (Test-Path "EasyDispatch.PerformanceTests.csproj")) {
    Write-Error "Error: Must run from EasyDispatch.PerformanceTests directory"
    exit 1
}

Write-Info "======================================"
Write-Info "EasyDispatch Performance Benchmarks"
Write-Info "======================================"
Write-Info ""

# Build in Release mode
Write-Info "Building in Release mode..."
dotnet build -c Release | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

Write-Success "Build successful!"
Write-Info ""

# Determine filter based on suite
$filter = switch ($Suite) {
    "All" { "*" }
    "Handler" { "*HandlerExecutionBenchmarks*" }
    "Notification" { "*NotificationStrategyBenchmarks*" }
    "Streaming" { "*StreamingQueryBenchmarks*" }
    "Behavior" { "*BehaviorOverheadBenchmarks*" }
    "ColdStart" { "*ColdStartBenchmarks*" }
    "Concurrent" { "*ConcurrentExecutionBenchmarks*" }
    "Comparison" { "*ComprehensiveComparisonBenchmark*" }
}

Write-Info "Running benchmark suite: $Suite"
Write-Info "Filter: $filter"
Write-Info ""

# Build command
$command = "dotnet run -c Release"

# Add filter
if ($filter -ne "*") {
    $command += " --filter `"$filter`""
}

# Add exporters if requested
if ($Export -or $Compare) {
    $command += " --exporters json,html,markdown"
}

# Run benchmarks
Write-Info "Executing benchmarks..."
Write-Info "Command: $command"
Write-Info ""

Invoke-Expression $command

if ($LASTEXITCODE -ne 0) {
    Write-Error "Benchmark execution failed!"
    exit 1
}

Write-Success "Benchmarks completed!"
Write-Info ""

# Handle comparison if requested
if ($Compare) {
    Write-Info "Comparing with baseline..."
    
    $resultsDir = "BenchmarkDotNet.Artifacts/results"
    
    if (-not (Test-Path $resultsDir)) {
        Write-Error "Results directory not found!"
        exit 1
    }
    
    # Find latest JSON results
    $latestResults = Get-ChildItem -Path $resultsDir -Filter "*-report.json" | 
                     Sort-Object LastWriteTime -Descending | 
                     Select-Object -First 1
    
    if (-not $latestResults) {
        Write-Error "No JSON results found!"
        exit 1
    }
    
    Write-Info "Latest results: $($latestResults.Name)"
    
    # Check for baseline
    $baselineFile = Join-Path $BaselinePath "$($latestResults.Name)"
    
    if (Test-Path $baselineFile) {
        Write-Info "Baseline found: $baselineFile"
        Write-Info ""
        Write-Info "Comparison:"
        Write-Info "----------------------------------------"
        
        # Simple comparison (you can enhance this)
        $current = Get-Content $latestResults.FullName | ConvertFrom-Json
        $baseline = Get-Content $baselineFile | ConvertFrom-Json
        
        Write-Info "Current benchmarks: $($current.Benchmarks.Count)"
        Write-Info "Baseline benchmarks: $($baseline.Benchmarks.Count)"
        Write-Info ""
        Write-Success "See detailed comparison in BenchmarkDotNet.Artifacts/results/"
    }
    else {
        Write-Info "No baseline found. Saving current results as baseline..."
        
        if (-not (Test-Path $BaselinePath)) {
            New-Item -ItemType Directory -Path $BaselinePath | Out-Null
        }
        
        Copy-Item $latestResults.FullName -Destination $baselineFile
        Write-Success "Baseline saved to: $baselineFile"
    }
}

Write-Info ""
Write-Info "======================================"
Write-Success "All operations completed successfully!"
Write-Info "======================================"
Write-Info ""

if (Test-Path "BenchmarkDotNet.Artifacts/results") {
    Write-Info "Results location: BenchmarkDotNet.Artifacts/results"
    Write-Info ""
    
    # List result files
    $resultFiles = Get-ChildItem -Path "BenchmarkDotNet.Artifacts/results" -Filter "*-report.*"
    Write-Info "Generated reports:"
    foreach ($file in $resultFiles) {
        Write-Info "  - $($file.Name)"
    }
}

Write-Info ""
Write-Info "Quick commands:"
Write-Info "  View HTML report:  start BenchmarkDotNet.Artifacts/results/*-report.html"
Write-Info "  View Markdown:     cat BenchmarkDotNet.Artifacts/results/*-report.md"
Write-Info ""