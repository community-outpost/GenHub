# ModBuilder Performance Benchmark & Regression Suite: Handover Document

**Target Branch**: [`perf/modbuilder-benchmark-suite`](https://github.com/undead2146/GenHub/tree/perf/modbuilder-benchmark-suite)  
**Repository**: `https://github.com/undead2146/GenHub.git`  
**Location**: `Benchmarks/ModBuilderPerformanceSuite/`  
**Purpose**: Run single-thread & multi-core performance comparisons between:
1. **Original Python ModBuilder**: [`TheSuperHackers/GeneralsModBuilder`](https://github.com/TheSuperHackers/GeneralsModBuilder)
2. **Community Go ModBuilder Port**: [`Polypheides/GoModBuilder`](https://github.com/Polypheides/GoModBuilder)
3. **C# ModBuilder Engine**: `GenHub` (`GenHub.Core`, `Features/Tools/ModBuilder`, `GenHub.Benchmarks`)

---

## 1. Prerequisites on the Test Machine

Ensure the target system has the following toolchains installed:

```bash
# 1. Python 3.10+ and required packages
sudo apt update && sudo apt install -y python3 python3-pip python3-tk
pip3 install Pillow psd-tools beeprint markdownmaker platformdirs PyYAML

# 2. Go 1.18+
sudo apt install -y golang-go

# 3. .NET 8.0 SDK
sudo apt install -y dotnet-sdk-8.0
```

---

## 2. Directory Layout & Setup

Clone the repositories into a shared workspace directory (e.g. `~/workspaces/`):

```bash
mkdir -p ~/workspaces && cd ~/workspaces

# 1. Clone GenHub and checkout benchmark branch
git clone https://github.com/undead2146/GenHub.git
cd GenHub
git checkout perf/modbuilder-benchmark-suite

# 2. Clone Original Python ModBuilder
cd ~/workspaces
git clone https://github.com/TheSuperHackers/GeneralsModBuilder.git

# 3. Clone Community Go ModBuilder
git clone https://github.com/Polypheides/GoModBuilder.git GenHub/.gomodbuilder_ref
```

---

## 3. Building the Benchmark Runners

Compile the Go binaries into the suite's `bin/` directory:

```bash
mkdir -p ~/workspaces/GenHub/Benchmarks/ModBuilderPerformanceSuite/bin

# Build the official Polypheides/GoModBuilder binary
cd ~/workspaces/GenHub/.gomodbuilder_ref
go build -o ~/workspaces/GenHub/Benchmarks/ModBuilderPerformanceSuite/bin/GoModBuilder .

# Build the Go microbenchmark runner
cd ~/workspaces/GenHub/Benchmarks/ModBuilderPerformanceSuite
go build -o bin/modbuilder_go_runner go_runner.go
```

---

## 4. Benchmark Execution Commands

### A. Run Master Micro & Macro Benchmark Suite
Runs the full 5-stage benchmark suite (MD5, BIG packing, CSF compilation, Cache serialization, Image RGBA channel splitting, and cold/warm builds) across Tier 1 (Small) and Tier 2 (Medium) datasets:

```bash
python3 ~/workspaces/GenHub/Benchmarks/ModBuilderPerformanceSuite/master_benchmark_orchestrator.py \
    --workspace ~/workspaces \
    --out /tmp/modbuilder_benchmark_results \
    --iterations 10
```

### B. Run Real Start-to-Finish Use Case Benchmark
Executes the actual command-line binaries on an authentic C&C Generals mod project containing 50 INI rules, 20 TGAs, and 20 WAV audio files:

```bash
python3 ~/workspaces/GenHub/Benchmarks/ModBuilderPerformanceSuite/run_real_usecase_benchmark.py
```

### C. Run Noise-Immune CPU Verification
Uses Linux kernel `getrusage()` process instruction counters (`ru_utime + ru_stime`) to completely eliminate multi-core or VPS CPU scheduling noise:

```bash
python3 ~/workspaces/GenHub/Benchmarks/ModBuilderPerformanceSuite/verify_cpu_times.py
```

---

## 5. Expected Performance Baselines & Regression Boundaries

| Workload | Python Baseline | Go Port (`Polypheides`) | C# Port (`GenHub`) | Speedup Ratio ($S$) | Parity Requirement |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Clean Cold Build (CLI)** | ~1,400 ms | ~5–10 ms | **~10–100 ms** | **>10x faster** | Valid BIG4 archive output |
| **Warm Incremental Build** | ~1,500 ms | ~10 ms | **<1 ms** | **>1,000x faster**| Zero file re-conversions |
| **MD5 File Hashing** | ~130–390 MB/s | ~150–450 MB/s | **~350–400 MB/s** | **Parity / Faster** | Bit-exact hash identity |
| **BIG Archive Creation** | ~65–365 MB/s | ~40–320 MB/s | **~70–365 MB/s** | **Parity / Faster** | 100% SHA-256 payload match |
| **CSF String Compilation**| ~15k–66k /s | ~7k–23k /s | **~28k–72k /s** | **>1.1x–1.9x** | Decrypted `~c` UTF-16LE match |
| **Cache Serialization** | ~100k–1.2M /s | ~24k–82k /s | **~480k–1.6M /s**| **>1.3x–4.6x** | Exact cache state match |

---

## 6. Output Files & Results
- Telemetry JSON: `/tmp/modbuilder_benchmark_results/benchmark_results.json`
- Markdown Report: `/tmp/modbuilder_benchmark_results/BENCHMARK_REPORT.md`
- Standalone HTML Dashboard: Viewable locally or uploaded via Postplan.
