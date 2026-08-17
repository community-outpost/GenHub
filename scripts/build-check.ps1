param(
    [string]$Project = ""
)

$ErrorActionPreference = "Stop"

if ($Project) {
    Write-Host "Building project: $Project"
    dotnet build $Project
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Project -match "Test") {
        Write-Host "Running tests for project: $Project"
        dotnet test $Project --no-build
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
} else {
    Write-Host "Building GenHub solution..."
    dotnet build GenHub/GenHub.sln
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Running Core unit tests..."
    dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj --filter "FullyQualifiedName~UserData|FullyQualifiedName~GeneralsOnline"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
