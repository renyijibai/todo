param(
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
if (!$sdk) { throw 'Install the .NET 10 SDK and add dotnet to PATH.' }
$project = Join-Path $PSScriptRoot 'src\SeeYou\SideNote.csproj'
& $sdk.Source build $project -c $Configuration -p:DebugType=None --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$destination = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$executable = Join-Path $PSScriptRoot "src\SeeYou\bin\$Configuration\net48\see you.exe"
Copy-Item -LiteralPath $executable -Destination (Join-Path $destination 'see you.exe') -Force
Write-Host "Ready: $destination\see you.exe"
