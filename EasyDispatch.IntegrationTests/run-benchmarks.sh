#!/bin/bash
# Bash script to run EasyDispatch performance benchmarks
# Usage: ./run-benchmarks.sh [suite] [--compare] [--export]

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default values
SUITE="All"
COMPARE=false
EXPORT=false
BASELINE_PATH="baseline"

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --compare)
            COMPARE=true
            EXPORT=true
            shift
            ;;
        --export)
            EXPORT=true
            shift
            ;;
        --baseline)
            BASELINE_PATH="$2"
            shift 2
            ;;
        Handler|Notification|Streaming|Behavior|ColdStart|Concurrent|Comparison|All)
            SUITE="$1"
            shift
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

function print_info {
    echo -e "${CYAN}$1${NC}"
}

function print_success {
    echo -e "${GREEN}$1${NC}"
}

function print_error {
    echo -e "${RED}$1${NC}"
}

# Check if we're in the right directory
if [ ! -f "EasyDispatch.PerformanceTests.csproj" ]; then
    print_error "Error: Must run from EasyDispatch.PerformanceTests directory"
    exit 1
fi

print_info "======================================"
print_info "EasyDispatch Performance Benchmarks"
print_info "======================================"
echo ""

# Build in Release mode
print_info "Building in Release mode..."
dotnet build -c Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    print_error "Build failed!"
    exit 1
fi

print_success "Build successful!"
echo ""

# Determine filter based on suite
case $SUITE in
    All)
        FILTER="*"
        ;;
    Handler)
        FILTER="*HandlerExecutionBenchmarks*"
        ;;
    Notification)
        FILTER="*NotificationStrategyBenchmarks*"
        ;;
    Streaming)
        FILTER="*StreamingQueryBenchmarks*"
        ;;
    Behavior)
        FILTER="*BehaviorOverheadBenchmarks*"
        ;;
    ColdStart)
        FILTER="*ColdStartBenchmarks*"
        ;;
    Concurrent)
        FILTER="*ConcurrentExecutionBenchmarks*"
        ;;
    Comparison)
        FILTER="*ComprehensiveComparisonBenchmark*"
        ;;
esac

print_info "Running benchmark suite: $SUITE"
print_info "Filter: $FILTER"
echo ""

# Build command
CMD="dotnet run -c Release"

# Add filter
if [ "$FILTER" != "*" ]; then
    CMD="$CMD --filter \"$FILTER\""
fi

# Add exporters if requested
if [ "$EXPORT" = true ]; then
    CMD="$CMD --exporters json,html,markdown"
fi

# Run benchmarks
print_info "Executing benchmarks..."
print_info "Command: $CMD"
echo ""

eval $CMD

if [ $? -ne 0 ]; then
    print_error "Benchmark execution failed!"
    exit 1
fi

print_success "Benchmarks completed!"
echo ""

# Handle comparison if requested
if [ "$COMPARE" = true ]; then
    print_info "Comparing with baseline..."
    
    RESULTS_DIR="BenchmarkDotNet.Artifacts/results"
    
    if [ ! -d "$RESULTS_DIR" ]; then
        print_error "Results directory not found!"
        exit 1
    fi
    
    # Find latest JSON results
    LATEST_RESULTS=$(ls -t $RESULTS_DIR/*-report.json 2>/dev/null | head -n1)
    
    if [ -z "$LATEST_RESULTS" ]; then
        print_error "No JSON results found!"
        exit 1
    fi
    
    print_info "Latest results: $(basename $LATEST_RESULTS)"
    
    # Check for baseline
    BASELINE_FILE="$BASELINE_PATH/$(basename $LATEST_RESULTS)"
    
    if [ -f "$BASELINE_FILE" ]; then
        print_info "Baseline found: $BASELINE_FILE"
        echo ""
        print_info "Comparison:"
        print_info "----------------------------------------"
        
        # Simple comparison
        CURRENT_COUNT=$(jq '.Benchmarks | length' "$LATEST_RESULTS")
        BASELINE_COUNT=$(jq '.Benchmarks | length' "$BASELINE_FILE")
        
        print_info "Current benchmarks: $CURRENT_COUNT"
        print_info "Baseline benchmarks: $BASELINE_COUNT"
        echo ""
        print_success "See detailed comparison in BenchmarkDotNet.Artifacts/results/"
    else
        print_info "No baseline found. Saving current results as baseline..."
        
        mkdir -p "$BASELINE_PATH"
        cp "$LATEST_RESULTS" "$BASELINE_FILE"
        
        print_success "Baseline saved to: $BASELINE_FILE"
    fi
fi

echo ""
print_info "======================================"
print_success "All operations completed successfully!"
print_info "======================================"
echo ""

if [ -d "BenchmarkDotNet.Artifacts/results" ]; then
    print_info "Results location: BenchmarkDotNet.Artifacts/results"
    echo ""
    
    # List result files
    print_info "Generated reports:"
    for file in BenchmarkDotNet.Artifacts/results/*-report.*; do
        if [ -f "$file" ]; then
            print_info "  - $(basename $file)"
        fi
    done
fi

echo ""
print_info "Quick commands:"
print_info "  View HTML report:  open BenchmarkDotNet.Artifacts/results/*-report.html"
print_info "  View Markdown:     cat BenchmarkDotNet.Artifacts/results/*-report.md"
echo ""