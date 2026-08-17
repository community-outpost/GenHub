param(
    [string]$Project = ""
)

$ErrorActionPreference = "Stop"

if ($Project) {
    Write-Host "Building project: $Project"
    dotnet build $Project
} else {
    Write-Host "Building GenHub solution..."
    dotnet build GenHub/GenHub.sln
    Write-Host "Running Core unit tests..."
    dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj --filter "FullyQualifiedName~UserData|FullyQualifiedName~GeneralsOnline"
}
