#!/usr/bin/env bash
# deploy-local.sh — Pull a specific image from GHCR and deploy it locally.
#
# Usage:
#   ./scripts/deploy-local.sh sha-abc1234
#   ./scripts/deploy-local.sh latest
#
# Prerequisites:
#   - Docker Desktop running
#   - Logged in to GHCR:
#       echo "$GHCR_PAT" | docker login ghcr.io -u YOUR_USERNAME --password-stdin
#
# Environment variables (override defaults):
#   GHCR_IMAGE_REPO   Full registry/owner/repo (without tag)
#                     Default: ghcr.io/tayyabnazeersha ikh/lrucache
#
# Rollback behaviour:
#   If the new version fails its health check, the script automatically
#   re-deploys the previous known-good version recorded in .deployed-tag.
#   You can also roll back manually:
#       ./scripts/deploy-local.sh sha-<previous-sha>

set -euo pipefail

# ─── Configuration ────────────────────────────────────────────────────────────

COMPOSE_FILE="compose.deploy.yml"
HEALTH_URL="http://localhost:8080/health"
PREVIOUS_TAG_FILE=".deployed-tag"   # persists the last known-good tag (gitignored)
HEALTH_RETRIES=12                   # total attempts
HEALTH_INTERVAL=5                   # seconds between attempts (12 × 5 = 60 s max wait)

# Allow override via environment variable for different repos/forks.
IMAGE_REPO="${GHCR_IMAGE_REPO:-ghcr.io/tayyabnazeersha ikh/lrucache}"

# ─── Argument parsing ─────────────────────────────────────────────────────────

if [[ $# -lt 1 ]]; then
    echo ""
    echo "  Usage: $0 <image-tag>"
    echo ""
    echo "  Examples:"
    echo "    $0 sha-abc1234     # deploy a specific CI-verified commit"
    echo "    $0 latest          # deploy the latest main-branch build"
    echo ""
    exit 1
fi

NEW_TAG="$1"
NEW_IMAGE="${IMAGE_REPO}:${NEW_TAG}"

echo ""
echo "═══════════════════════════════════════════════════════"
echo "  LruCache Local Deployment"
echo "  Image: ${NEW_IMAGE}"
echo "═══════════════════════════════════════════════════════"

# ─── Read previous (known-good) tag ──────────────────────────────────────────
# The .deployed-tag file records the last successfully deployed tag.
# It is written by this script on success and read on failure for rollback.

if [[ -f "${PREVIOUS_TAG_FILE}" ]]; then
    PREVIOUS_TAG=$(cat "${PREVIOUS_TAG_FILE}" | tr -d '[:space:]')
    echo "  Previous tag: ${PREVIOUS_TAG}"
else
    PREVIOUS_TAG=""
    echo "  No previous deployment recorded."
fi

echo ""

# ─── Prevent deploying the same tag twice ─────────────────────────────────────

if [[ "${PREVIOUS_TAG}" == "${NEW_TAG}" ]]; then
    echo "ℹ  Already running ${NEW_TAG}. Nothing to do."
    exit 0
fi

# ─── Pull image from GHCR ─────────────────────────────────────────────────────
# We pull before stopping the old version so the new image is ready locally.
# If the pull fails (bad tag, no network, not logged in), the old version
# continues running uninterrupted.

echo "⬇  Pulling ${NEW_IMAGE}..."
if ! docker pull "${NEW_IMAGE}"; then
    echo ""
    echo "❌ Pull failed. The current version continues running."
    echo "   Check: docker login ghcr.io -u YOUR_USERNAME --password-stdin"
    exit 1
fi

echo ""

# ─── Deploy new version ───────────────────────────────────────────────────────
# docker compose up -d recreates the container if the image changed.
# The old container is stopped and removed; the new one starts.

echo "🚀 Starting ${NEW_TAG}..."
export IMAGE_TAG="${NEW_IMAGE}"
docker compose -f "${COMPOSE_FILE}" up -d --remove-orphans

echo ""

# ─── Health check ─────────────────────────────────────────────────────────────
# Poll /health until it returns HTTP 200 or we exhaust retries.
# The start_period in compose.deploy.yml allows for JIT warmup.

echo "🔍 Health check — polling ${HEALTH_URL}"
echo "   (${HEALTH_RETRIES} attempts × ${HEALTH_INTERVAL}s = $((HEALTH_RETRIES * HEALTH_INTERVAL))s max)"
echo ""

HEALTHY=false
for i in $(seq 1 "${HEALTH_RETRIES}"); do
    HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 4 "${HEALTH_URL}" 2>/dev/null || echo "000")

    if [[ "${HTTP_STATUS}" == "200" ]]; then
        HEALTHY=true
        echo "  ✅ Attempt ${i}/${HEALTH_RETRIES}: HTTP ${HTTP_STATUS} — Healthy!"
        break
    fi

    echo "  ⏳ Attempt ${i}/${HEALTH_RETRIES}: HTTP ${HTTP_STATUS} — waiting ${HEALTH_INTERVAL}s..."
    sleep "${HEALTH_INTERVAL}"
done

echo ""

# ─── Success ──────────────────────────────────────────────────────────────────

if [[ "${HEALTHY}" == "true" ]]; then
    echo "${NEW_TAG}" > "${PREVIOUS_TAG_FILE}"
    echo "✅ Deployment SUCCEEDED"
    echo "   Running:  ${NEW_TAG}"
    echo ""
    exit 0
fi

# ─── Failure + automatic rollback ─────────────────────────────────────────────

echo "❌ Deployment FAILED — application did not become healthy."
echo ""

if [[ -n "${PREVIOUS_TAG}" ]]; then
    PREVIOUS_IMAGE="${IMAGE_REPO}:${PREVIOUS_TAG}"
    echo "⏪ Rolling back to previous version: ${PREVIOUS_TAG}"
    echo "   Pulling ${PREVIOUS_IMAGE}..."

    if docker pull "${PREVIOUS_IMAGE}"; then
        export IMAGE_TAG="${PREVIOUS_IMAGE}"
        docker compose -f "${COMPOSE_FILE}" up -d --remove-orphans
        echo ""
        echo "✅ Rollback SUCCEEDED. Running: ${PREVIOUS_TAG}"
        echo "   The failed image (${NEW_TAG}) remains locally — remove it with:"
        echo "   docker rmi ${NEW_IMAGE}"
    else
        echo "❌ Rollback failed — could not pull ${PREVIOUS_IMAGE}"
        echo "   Bringing containers down to avoid running a broken version."
        docker compose -f "${COMPOSE_FILE}" down
    fi
else
    echo "⚠  No previous version recorded. Bringing containers down."
    docker compose -f "${COMPOSE_FILE}" down
fi

echo ""
exit 1
