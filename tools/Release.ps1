<#
  Build, sign and (optionally) publish a release.

  1. Raise <Version> in ddeprivedd.os.csproj  (for example 3.1.0.0 -> 3.1.1.0)
  2. Run:   powershell -ExecutionPolicy Bypass -File tools\Release.ps1
     It builds, then writes dist\ddeprivedd.os.exe and dist\ddeprivedd.os.exe.sig
  3. Upload those TWO files to a new GitHub release tagged v<version> (for example v3.1.1).
     Or let this script do it:   tools\Release.ps1 -Publish -Notes "What changed"
     (needs the GitHub CLI: https://cli.github.com, then run "gh auth login" once)

  Everyone running the app picks the update up automatically.
#>
param([switch]$Publish, [string]$Notes = '')

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$proj    = Join-Path $root 'ddeprivedd.os.csproj'
$dist    = Join-Path $root 'dist'
$keyFile = Join-Path $env:USERPROFILE 'ddeprivedd-signing-key\private-key.xml'

if (-not (Test-Path $keyFile)) { throw "No signing key found. Run tools\Setup-Updates.ps1 first." }
if ((Get-Content (Join-Path $root 'Core\UpdateConfig.cs') -Raw) -match 'PublicKeyXml = "";') {
    throw "Core\UpdateConfig.cs isn't configured yet. Run tools\Setup-Updates.ps1 first."
}

[xml]$xml = Get-Content $proj
$version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Couldn't find <Version> in the .csproj" }
$tag = 'v' + ($version -replace '\.0$', '')

Write-Host "Building version $version (release tag $tag)..."
& dotnet build $proj -c Release -o $dist
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$exe = Join-Path $dist 'ddeprivedd.os.exe'
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($keyFile))
$sha = New-Object System.Security.Cryptography.SHA256CryptoServiceProvider
$sig = $rsa.SignData([IO.File]::ReadAllBytes($exe), $sha)
[IO.File]::WriteAllText("$exe.sig", [Convert]::ToBase64String($sig))

Write-Host ""
Write-Host "Signed. Files to upload to the GitHub release '$tag':" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "  $exe.sig"

if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) not found. Install it from https://cli.github.com or upload the two files by hand." }
    & gh release create $tag $exe "$exe.sig" --title "ddeprivedd.os $tag" --notes $Notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
    Write-Host "Published $tag." -ForegroundColor Green
}
