# deploy-local.ps1 — Pull a specific image from GHCR and deploy it locally.
#
# Usage:
#   .\scripts\deploy-local.ps1 -Tag sha-abc1234
#   .\scripts\deploy-local.ps1 -Tag latest
#
# Prerequisites:
#   - Docker Desktop running
#   - Logged in to GHCR:
#       $env:GHCR_PAT | docker login ghcr.io -u YOUR_USERNAME --password-stdin
#
# Rollback:
#   If the new version fails health checks, the previous known-good version
#   in .deployed-tag is re-deployed automatically.
#   Manual rollback: .\scripts\deploy-local.ps1 -Tag sha-<previous-sha>

param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $false)]
    [string]$ImageRepo = ""
)

$ErrorActionPreference = "Stop"

# ─── Configuration ────────────────────────────────────────────────────────────

$ComposeFile    = "compose.deploy.yml"
$HealthUrl      = "http://localhost:8080/health"
$PreviousTagFile= ".deployed-tag"
$HealthRetries  = 12
$HealthInterval = 5     # seconds between attempts

if (-not $ImageRepo) {
    $ImageRepo = if ($env:GHCR_IMAGE_REPO) { $env:GHCR_IMAGE_REPO }
                 else { "ghcr.io/tayyabnazeersha ikh/lrucache" }
}

$NewImage = "${ImageRepo}:${Tag}"

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════"
Write-Host "  LruCache Local Deployment"
Write-Host "  Image: $NewImage"
Write-Host "═══════════════════════════════════════════════════════"

# ─── Read previous tag ────────────────────────────────────────────────────────

$PreviousTag = ""
if (Test-Path $PreviousTagFile) {
    $PreviousTag = (Get-Content $PreviousTagFile -Raw).Trim()
    Write-Host "  Previous tag: $PreviousTag"
} else {
    Write-Host "  No previous deployment recorded."
}
Write-Host ""

# ─── Prevent no-op ────────────────────────────────────────────────────────────

if ($PreviousTag -eq $Tag) {
    Write-Host "Already running $Tag. Nothing to do."
    exit 0
}

# ─── Pull image ───────────────────────────────────────────────────────────────

Write-Host "Pulling $NewImage..."
docker pull $NewImage
if ($LASTEXITCODE -ne 0) {
    Write-Error "Pull failed. The current version continues running."
    Write-Host "Check: docker login ghcr.io -u YOUR_USERNAME --password-stdin"
    exit 1
}
Write-Host ""

# ─── Deploy ───────────────────────────────────────────────────────────────────

Write-Host "Starting $Tag..."
$env:IMAGE_TAG = $NewImage
docker compose -f $ComposeFile up -d --remove-orphans
if ($LASTEXITCODE -ne 0) {
    Write-Error "docker compose failed."
    exit 1
}
Write-Host ""

# ─── Health check ─────────────────────────────────────────────────────────────

Write-Host "Health check — polling $HealthUrl"
Write-Host "  ($HealthRetries attempts x ${HealthInterval}s = $($HealthRetries * $HealthInterval)s max)"
Write-Host ""

$Healthy = $false
for ($i = 1; $i -le $HealthRetries; $i++) {
    try {
        $Response = Invoke-WebRequest -Uri $HealthUrl -Method GET `
            -TimeoutSec 4 -UseBasicParsing -ErrorAction Stop
        if ($Response.StatusCode -eq 200) {
            $Healthy = $true
            Write-Host "  Attempt $i/$HealthRetries : HTTP $($Response.StatusCode) - Healthy!"
            break
        }
    } catch {
        Write-Host "  Attempt $i/$HealthRetries : not ready - waiting ${HealthInterval}s..."
        Start-Sleep -Seconds $HealthInterval
    }
}
Write-Host ""

# ─── Success ──────────────────────────────────────────────────────────────────

if ($Healthy) {
    Set-Content -Path $PreviousTagFile -Value $Tag -NoNewline
    Write-Host "Deployment SUCCEEDED"
    Write-Host "  Running: $Tag"
    exit 0
}

# ─── Failure + rollback ───────────────────────────────────────────────────────

Write-Error "Deployment FAILED — application did not become healthy."
Write-Host ""

if ($PreviousTag -ne "") {
    $PreviousImage = "${ImageRepo}:${PreviousTag}"
    Write-Host "Rolling back to previous version: $PreviousTag"
    docker pull $PreviousImage
    if ($LASTEXITCODE -eq 0) {
        $env:IMAGE_TAG = $PreviousImage
        docker compose -f $ComposeFile up -d --remove-orphans
        Write-Host ""
        Write-Host "Rollback SUCCEEDED. Running: $PreviousTag"
        Write-Host "The failed image ($Tag) remains locally. Remove with:"
        Write-Host "  docker rmi $NewImage"
    } else {
        Write-Error "Rollback failed — could not pull $PreviousImage"
        Write-Host "Bringing containers down."
        docker compose -f $ComposeFile down
    }
} else {
    Write-Warning "No previous version recorded. Bringing containers down."
    docker compose -f $ComposeFile down
}

exit 1
