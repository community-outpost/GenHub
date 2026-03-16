param(
    [string]$Token
)

if ([string]::IsNullOrEmpty($Token)) {
    # For PRs from forks, secrets are not available. We use a dummy token to allow the build to pass.
    if ($env:GITHUB_EVENT_NAME -eq 'pull_request') {
        Write-Host "Warning: No UPLOADTHING_TOKEN provided. Using dummy token for PR build."
        $Token = "DUMMY_TOKEN_FOR_CI_ONLY"
    } else {
        Write-Error "No UPLOADTHING_TOKEN provided. Fails for Release builds."
        exit 1
    }
}

$constantsPath = "GenHub/GenHub.Core/Constants/ApiConstants.cs"
if (-not (Test-Path $constantsPath)) {
    Write-Error "Could not find $constantsPath"
    exit 1
}

$tokenBytes = [System.Text.Encoding]::UTF8.GetBytes($Token)
$key = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($key)

$obfuscated = New-Object byte[] $tokenBytes.Length
for ($i = 0; $i -lt $tokenBytes.Length; $i++) {
    $obfuscated[$i] = $tokenBytes[$i] -bxor $key[$i % $key.Length]
}

$dataStr = ($obfuscated | ForEach-Object { "0x{0:x2}" -f $_ }) -join ", "
$keyStr = ($key | ForEach-Object { "0x{0:x2}" -f $_ }) -join ", "

$content = Get-Content $constantsPath -Raw

# Use regex replace to be robust against whitespace
# Pattern: byte[] data = []; // [PLACEHOLDER_DATA]
$content = [System.Text.RegularExpressions.Regex]::Replace($content, 'byte\[\]\s+data\s*=\s*\[\];\s*//\s*\[PLACEHOLDER_DATA\]', "byte[] data = [$dataStr];")
$content = [System.Text.RegularExpressions.Regex]::Replace($content, 'byte\[\]\s+key\s*=\s*\[\];\s*//\s*\[PLACEHOLDER_KEY\]', "byte[] key = [$keyStr];")

if ($content -notmatch "0x") {
    Write-Error "Token injection failed! Placeholders were not found or replaced."
    exit 1
}

Set-Content $constantsPath $content
Write-Host "Successfully injected and obfuscated UPLOADTHING_TOKEN into ApiConstants.cs"
