<#
  ONE-TIME SETUP for auto-update.

  Usage (from the project folder):
      powershell -ExecutionPolicy Bypass -File tools\Setup-Updates.ps1 -Repo yourname/ddeprivedd-os

  What it does:
    1. Creates your private signing key in  %USERPROFILE%\ddeprivedd-signing-key\private-key.xml
       (kept OUTSIDE the project so it can never be uploaded by accident - BACK IT UP, and never share it).
    2. Writes your GitHub repo and the matching PUBLIC key into Core\UpdateConfig.cs.
  Run it again later and it reuses the same key.
#>
param([Parameter(Mandatory = $true)][string]$Repo)

$ErrorActionPreference = 'Stop'
if ($Repo -notmatch '^[\w.-]+/[\w.-]+$') { throw "Use the form owner/repo, for example: yourname/ddeprivedd-os" }
$owner, $name = $Repo.Split('/')

$root   = Split-Path -Parent $PSScriptRoot
$cfg    = Join-Path $root 'Core\UpdateConfig.cs'
$keyDir = Join-Path $env:USERPROFILE 'ddeprivedd-signing-key'
$keyFile = Join-Path $keyDir 'private-key.xml'
New-Item -ItemType Directory -Force -Path $keyDir | Out-Null

if (Test-Path $keyFile) {
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.FromXmlString([IO.File]::ReadAllText($keyFile))
    Write-Host "Using your existing signing key: $keyFile"
} else {
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(3072)
    [IO.File]::WriteAllText($keyFile, $rsa.ToXmlString($true))
    Write-Host "Created a new signing key: $keyFile"
    Write-Host "BACK THIS FILE UP somewhere safe. If you lose it, existing installs can never accept updates again." -ForegroundColor Yellow
}
$pub = $rsa.ToXmlString($false)

$lines = Get-Content -Path $cfg | ForEach-Object {
    if     ($_ -match '<<REPO_OWNER>>') { '        public const string RepoOwner = "{0}";   // <<REPO_OWNER>>' -f $owner }
    elseif ($_ -match '<<REPO_NAME>>')  { '        public const string RepoName = "{0}";    // <<REPO_NAME>>' -f $name }
    elseif ($_ -match '<<PUBLIC_KEY>>') { '        public const string PublicKeyXml = "{0}";   // <<PUBLIC_KEY>>' -f $pub }
    else { $_ }
}
Set-Content -Path $cfg -Value $lines -Encoding UTF8

Write-Host ""
Write-Host "Done. Core\UpdateConfig.cs now points at github.com/$owner/$name." -ForegroundColor Green
Write-Host "Next: build and publish your first release with  tools\Release.ps1"
