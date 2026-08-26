<#
.SYNOPSIS
    Serialized build/check script for GenHub. Prevents build conflicts when
    multiple agents work simultaneously and avoids builds during debugging.

.DESCRIPTION
    Uses a named mutex to ensure only one build runs at a time.
    Detects active debugger (devenv lock on output DLLs) and refuses to build.
    Supports a lightweight "check" mode that only compiles without producing output.

.PARAMETER Mode
    "check"   - Lightweight: compile-only, no output, fastest (default)
    "build"   - Full build with output
    "restore" - NuGet restore only

.PARAMETER Project
    Specific .csproj to check. Defaults to the full solution.
    Pass a project path relative to the GenHub solution folder for faster checks.
    Example: "GenHub.Core/GenHub.Core.csproj"

.PARAMETER TimeoutSeconds
    Max seconds to wait for the build mutex. Default: 120

.PARAMETER Verbosity
    MSBuild verbosity: quiet, minimal, normal, detailed. Default: quiet

.EXAMPLE
    # Quick error check on the full solution
    .\scripts\build-check.ps1

.EXAMPLE
    # Quick error check on a single project
    .\scripts\build-check.ps1 -Project "GenHub.Core/GenHub.Core.csproj"

.EXAMPLE
    # Full build (serialized, safe)
    .\scripts\build-check.ps1 -Mode build

.EXAMPLE
    # Check with longer timeout
    .\scripts\build-check.ps1 -TimeoutSeconds 300
#>

param(
    [ValidateSet("check", "build", "restore")]
    [string]$Mode = "check",

    [string]$Project = "",

    [int]$TimeoutSeconds = 120,

    [ValidateSet("quiet", "minimal", "normal", "detailed")]
    [string]$Verbosity = "quiet"
)

$ErrorActionPreference = "Stop"

# ── Constants ──────────────────────────────────────────────────────────────────
$MutexName       = "Global\GenHub_Build_Mutex"
$SolutionDir     = Join-Path (Join-Path $PSScriptRoot "..") "GenHub"
$SolutionFile    = Join-Path $SolutionDir "GenHub.sln"
$LockFileName    = "build.lock"
$LockFilePath    = Join-Path $SolutionDir $LockFileName

# ── Helper functions ───────────────────────────────────────────────────────────

function Write-Status {
    param([string]$Message, [string]$Color = "Cyan")
    Write-Host "[build-check] " -ForegroundColor DarkGray -NoNewline
    Write-Host $Message -ForegroundColor $Color
}

function Write-Err {
    param([string]$Message)
    Write-Host "[build-check] " -ForegroundColor DarkGray -NoNewline
    Write-Host "ERROR: $Message" -ForegroundColor Red
}

function Test-DebuggerActive {
    <#
    .SYNOPSIS
        Detects if Visual Studio is debugging GenHub by checking for file locks
        on the output DLLs in bin/Debug directories.
    #>

    # Check for devenv.exe processes that hold locks
    $devenvProcesses = Get-Process -Name "devenv" -ErrorAction SilentlyContinue
    if (-not $devenvProcesses) {
        return $false
    }

    # Check if GenHub output DLLs are locked (indicates active debugging)
    $binDebugDirs = Get-ChildItem -Path $SolutionDir -Directory -Recurse -Filter "Debug" |
        Where-Object { $_.Parent.Name -eq "bin" }

    foreach ($dir in $binDebugDirs) {
        $dlls = Get-ChildItem -Path $dir.FullName -Filter "GenHub*.dll" -ErrorAction SilentlyContinue
        foreach ($dll in $dlls) {
            try {
                # Try to open exclusively - if it fails, the file is locked (debugger)
                $stream = [System.IO.File]::Open($dll.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                $stream.Close()
                $stream.Dispose()
            }
            catch {
                # File is locked - debugger is likely active
                return $true
            }
        }
    }

    return $false
}

function Get-BuildTarget {
    if ($Project) {
        $projectPath = Join-Path $SolutionDir $Project
        if (-not (Test-Path $projectPath)) {
            Write-Err "Project not found: $projectPath"
            exit 1
        }
        return $projectPath
    }
    return $SolutionFile
}

# ── Pre-flight checks ─────────────────────────────────────────────────────────

if (-not (Test-Path $SolutionFile)) {
    Write-Err "Solution not found at: $SolutionFile"
    exit 1
}

# Check for debugger
if (Test-DebuggerActive) {
    Write-Err "Visual Studio debugger appears to be active (output DLLs are locked)."
    Write-Err "Cannot build while debugging. Detach the debugger first."
    exit 2
}

# ── Acquire mutex ──────────────────────────────────────────────────────────────

$mutex = $null
$acquired = $false

try {
    Write-Status "Acquiring build lock (timeout: ${TimeoutSeconds}s)..."

    $mutex = [System.Threading.Mutex]::new($false, $MutexName)
    try {
        $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
    }
    catch [System.Threading.AbandonedMutexException] {
        $acquired = $true
    }

    if (-not $acquired) {
        Write-Err "Timed out waiting for build lock after ${TimeoutSeconds}s."
        Write-Err "Another agent or process is currently building."
        exit 3
    }

    # Write lock file for visibility
    $lockInfo = @{
        pid       = $PID
        mode      = $Mode
        project   = if ($Project) { $Project } else { "GenHub.sln" }
        startedAt = (Get-Date -Format "o")
        agent     = $env:AGENT_NAME
    } | ConvertTo-Json -Compress
    Set-Content -Path $LockFilePath -Value $lockInfo -Force

    Write-Status "Build lock acquired." "Green"

    # ── Execute build ──────────────────────────────────────────────────────────

    $target = Get-BuildTarget
    $exitCode = 0

    switch ($Mode) {
        "check" {
            Write-Status "Running compile check on: $(Split-Path $target -Leaf)"

            # Use --no-restore to skip package resolution (much faster)
            # Use --no-dependencies when checking a single project (skip transitive)
            $args = @(
                "build", $target,
                "--no-restore",
                "--nologo",
                "--verbosity", $Verbosity,
                "-maxcpucount:2"
            )

            if ($Project) {
                $args += "--no-dependencies"
            }

            & dotnet @args
            $exitCode = $LASTEXITCODE
        }

        "build" {
            Write-Status "Running full build on: $(Split-Path $target -Leaf)"

            $args = @(
                "build", $target,
                "--nologo",
                "--verbosity", $Verbosity,
                "-maxcpucount:2"
            )

            & dotnet @args
            $exitCode = $LASTEXITCODE
        }

        "restore" {
            Write-Status "Running NuGet restore on: $(Split-Path $target -Leaf)"

            & dotnet restore $target --verbosity $Verbosity
            $exitCode = $LASTEXITCODE
        }
    }

    # ── Report result ──────────────────────────────────────────────────────────

    if ($exitCode -eq 0) {
        Write-Status "Completed successfully with no errors." "Green"
    }
    else {
        Write-Err "Build/check failed with exit code: $exitCode"
    }

    exit $exitCode
}
finally {
    # Clean up lock file
    if (Test-Path $LockFilePath) {
        Remove-Item $LockFilePath -Force -ErrorAction SilentlyContinue
    }

    # Release mutex
    if ($acquired -and $mutex) {
        $mutex.ReleaseMutex()
    }

    if ($mutex) {
        $mutex.Dispose()
    }
}
