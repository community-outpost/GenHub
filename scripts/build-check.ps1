param (
    [string]$Project = "GenHub/GenHub.sln",
    [ValidateSet("check", "build", "restore")]
    [string]$Mode = "check"
)

$mutexName = "Global\GenHubBuildLock"
$mutex = New-Object System.Threading.Mutex($false, $mutexName)

try {
    $hasHandle = $false
    try {
        $hasHandle = $mutex.WaitOne(60000, $false)
    } catch [System.Threading.AbandonedMutexException] {
        $hasHandle = $true
    }

    if (-not $hasHandle) {
        Write-Error "Timed out waiting for build mutex."
        exit 1
    }

    $targetPath = $Project
    if (-not (Test-Path $targetPath)) {
        if (Test-Path "GenHub/$Project") {
            $targetPath = "GenHub/$Project"
        }
    }

    switch ($Mode) {
        "check" {
            dotnet build $targetPath -c Debug -maxcpucount:2 /nologo
        }
        "build" {
            dotnet build $targetPath -c Release -maxcpucount:2 /nologo
        }
        "restore" {
            dotnet restore $targetPath /nologo
        }
    }
}
finally {
    if ($hasHandle) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
