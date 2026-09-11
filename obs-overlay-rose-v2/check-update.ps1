# Vérifie GitHub pour une mise à jour au lancement de start-overlay.
# Repo : https://github.com/Syckoy/Overlay_Keyboard-modern
# Préserve : settings.json + assets/user-*

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$e = [char]27
$ok = "$e[38;2;111;206;160m"
$warn = "$e[38;2;242;180;90m"
$bad = "$e[38;2;229;115;115m"
$info = "$e[38;2;229;107;138m"
$dim = "$e[38;2;160;160;170m"
$off = "$e[0m"

function Write-Info([string]$msg) { Write-Host "${info}[update]${off} $msg" }
function Write-Dim([string]$msg) { Write-Host "${dim}[update] $msg${off}" }
function Write-Warn([string]$msg) { Write-Host "${warn}[update] $msg${off}" }
function Write-Err([string]$msg) { Write-Host "${bad}[update] $msg${off}" }
function Write-Ok([string]$msg) { Write-Host "${ok}[update] $msg${off}" }

function Read-JsonFile([string]$path) {
  if (-not (Test-Path $path)) { return $null }
  try {
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
  } catch {
    return $null
  }
}

function Save-JsonFile([string]$path, $obj) {
  $json = $obj | ConvertTo-Json -Depth 8
  [System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-GitHubJson([string]$url) {
  $headers = @{
    "User-Agent" = "Overlay-Keyboard-Update-Check"
    "Accept"     = "application/vnd.github+json"
  }
  return Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 12
}

function Test-PreservedPath([string]$rel) {
  $n = $rel -replace "\\", "/"
  if ($n -ieq "settings.json") { return $true }
  if ($n -like "assets/user-*") { return $true }
  if ($n -ieq ".update-state.json") { return $true }
  if ($n -ieq "bridge.exe") { return $true }
  return $false
}

$versionPath = Join-Path $Root "version.json"
$statePath = Join-Path $Root ".update-state.json"
$version = Read-JsonFile $versionPath
if (-not $version) {
  $version = [pscustomobject]@{
    name    = "Overlay Keyboard Modern"
    version = "2.1.0"
    repo    = "Syckoy/Overlay_Keyboard-modern"
    branch  = "main"
    subdir  = "obs-overlay-rose-v2"
    commit  = ""
  }
}

$repo = [string]$version.repo
$branch = if ($version.branch) { [string]$version.branch } else { "main" }
$subdir = if ($version.subdir) { [string]$version.subdir } else { "obs-overlay-rose-v2" }
$state = Read-JsonFile $statePath
$localCommit = ""
if ($state -and $state.commit) { $localCommit = [string]$state.commit }
elseif ($version.commit) { $localCommit = [string]$version.commit }

Write-Info "Verification des mises a jour GitHub..."
Write-Dim "https://github.com/$repo"

try {
  $remoteCommit = $null
  $remoteMessage = ""
  $remoteDate = ""
  $releaseTag = $null

  # 1) Releases (si un jour tu en publies)
  try {
    $release = Get-GitHubJson "https://api.github.com/repos/$repo/releases/latest"
    if ($release -and $release.tag_name) {
      $releaseTag = [string]$release.tag_name
      $remoteMessage = [string]$release.name
      if (-not $remoteMessage) { $remoteMessage = $releaseTag }
      $remoteDate = [string]$release.published_at
      if ($release.target_commitish) {
        # On prendra aussi le SHA de la branche pour le suivi
      }
    }
  } catch {
    # Pas de release = normal pour l'instant
  }

  # 2) Commit HEAD de la branche (référence principale)
  $commitInfo = Get-GitHubJson "https://api.github.com/repos/$repo/commits/$branch"
  $remoteCommit = [string]$commitInfo.sha
  if (-not $remoteMessage) { $remoteMessage = [string]$commitInfo.commit.message }
  if (-not $remoteDate) { $remoteDate = [string]$commitInfo.commit.author.date }

  if (-not $remoteCommit) {
    Write-Warn "Impossible de lire le commit distant. Demarrage sans mise a jour."
    exit 0
  }

  $shortRemote = $remoteCommit.Substring(0, [Math]::Min(7, $remoteCommit.Length))
  $shortLocal = if ($localCommit.Length -ge 7) { $localCommit.Substring(0, 7) } else { "(aucune)" }

  # Premier lancement : on enregistre juste la reference GitHub, sans ecraser les fichiers locaux
  if (-not $localCommit) {
    $version.commit = $remoteCommit
    Save-JsonFile $versionPath $version
    Save-JsonFile $statePath ([pscustomobject]@{
      commit     = $remoteCommit
      checkedAt  = (Get-Date).ToString("o")
      releaseTag = $releaseTag
      note       = "baseline-init"
    })
    Write-Ok "Suivi active. Reference GitHub : $shortRemote"
    Write-Dim "Les prochaines nouveautes du repo te seront proposees ici."
    exit 0
  }

  if ($localCommit -ieq $remoteCommit) {
    Write-Ok "Deja a jour ($shortLocal)."
    exit 0
  }

  Write-Host ""
  Write-Warn "Une mise a jour est disponible !"
  Write-Host "${dim}  Locale :  $shortLocal${off}"
  Write-Host "${dim}  GitHub :  $shortRemote${off}"
  if ($releaseTag) { Write-Host "${dim}  Release : $releaseTag${off}" }
  if ($remoteMessage) {
    $oneLine = ($remoteMessage -split "`n")[0]
    Write-Host "${dim}  Notes  :  $oneLine${off}"
  }
  if ($remoteDate) { Write-Host "${dim}  Date   :  $remoteDate${off}" }
  Write-Host "${dim}  Repo   :  https://github.com/$repo${off}"
  Write-Host ""
  Write-Host "${warn}Tes reglages (settings.json) et images perso (assets/user-*) seront conserves.${off}"
  Write-Host ""

  $answer = Read-Host "Mettre a jour maintenant ? (O/N)"
  if ($answer -notmatch '^[oOyY]') {
    Write-Dim "Mise a jour ignoree. Tu pourras la faire au prochain lancement."
    exit 0
  }

  Write-Info "Telechargement de la mise a jour..."
  $tmp = Join-Path $env:TEMP ("overlay-upd-" + [guid]::NewGuid().ToString("N"))
  New-Item -ItemType Directory -Path $tmp | Out-Null
  $zip = Join-Path $tmp "repo.zip"
  $extract = Join-Path $tmp "extract"

  $zipUrl = "https://github.com/$repo/archive/refs/heads/$branch.zip"
  Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing -TimeoutSec 120

  Write-Info "Extraction..."
  Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force

  $srcRoot = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
  if (-not $srcRoot) { throw "Archive GitHub invalide." }
  $srcDir = Join-Path $srcRoot.FullName $subdir
  if (-not (Test-Path $srcDir)) {
    # Au cas ou le repo est a plat un jour
    $srcDir = $srcRoot.FullName
  }
  if (-not (Test-Path $srcDir)) { throw "Dossier source introuvable dans l'archive ($subdir)." }

  Write-Info "Application des fichiers..."
  $copied = 0
  Get-ChildItem -LiteralPath $srcDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($srcDir.Length).TrimStart("\", "/")
    if (Test-PreservedPath $rel) { return }
    $dest = Join-Path $Root $rel
    $destParent = Split-Path -Parent $dest
    if (-not (Test-Path $destParent)) {
      New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }
    Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    $copied++
  }

  # Met a jour le suivi de version
  $version.commit = $remoteCommit
  if ($releaseTag) { $version | Add-Member -NotePropertyName lastRelease -NotePropertyValue $releaseTag -Force }
  Save-JsonFile $versionPath $version
  Save-JsonFile $statePath ([pscustomobject]@{
    commit     = $remoteCommit
    checkedAt  = (Get-Date).ToString("o")
    releaseTag = $releaseTag
  })

  try { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch { }

  Write-Ok "Mise a jour terminee ($copied fichiers). Commit $shortRemote."
  Write-Dim "Le bridge sera recompile juste apres."
  Write-Host ""
}
catch {
  Write-Warn "Verification / mise a jour impossible : $($_.Exception.Message)"
  Write-Dim "Demarrage avec la version actuelle."
  exit 0
}
