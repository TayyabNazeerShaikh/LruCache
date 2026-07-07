# ─── Stage 1: Build ───────────────────────────────────────────────────────────
# The SDK image contains the full .NET toolchain: compilers, NuGet, MSBuild.
# It is large (~900 MB) but only used during the build — never shipped.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# ── Layer-cache optimisation ──────────────────────────────────────────────────
# Copy only the project files first and restore NuGet packages.
# Docker rebuilds a layer only when its inputs change.
# Because we copy .csproj files BEFORE the full source code, the restore layer
# stays cached on every code edit — it only re-runs when a .csproj changes.
COPY src/LruCache.Domain/LruCache.Domain.csproj             src/LruCache.Domain/
COPY src/LruCache.Application/LruCache.Application.csproj   src/LruCache.Application/
COPY src/LruCache.Infrastructure/LruCache.Infrastructure.csproj src/LruCache.Infrastructure/
COPY src/LruCache.Api/LruCache.Api.csproj                   src/LruCache.Api/

RUN dotnet restore src/LruCache.Api/LruCache.Api.csproj

# ── Build and publish ─────────────────────────────────────────────────────────
# Copy the full source after restore. Any code change only invalidates this layer
# and below — not the restore layer above.
COPY src/ src/

# --no-restore: packages are already restored above; skip the redundant check.
# --configuration Release: enables all compiler optimisations, no debug symbols.
# --output: collect the publish artefacts in a single known directory.
RUN dotnet publish src/LruCache.Api/LruCache.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ─── Stage 2: Runtime ─────────────────────────────────────────────────────────
# The ASP.NET Core runtime image contains only what is needed to RUN the app:
# no SDK, no compilers, no NuGet. Result: ~230 MB vs ~900 MB for the SDK image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for the HEALTHCHECK instruction below.
# This must run as root (before USER app). The resulting binary is world-executable.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Copy the published output from the build stage.
# --chown=app:app sets file ownership so the non-root user can read the files.
COPY --from=build --chown=app:app /app/publish .

# Switch to the non-root 'app' user (UID 1654) built into .NET 8+ runtime images.
# Running as root inside a container is a security risk: if the application is
# compromised, an attacker would have root access to the container.
USER app

# .NET 8+ base image sets ASPNETCORE_HTTP_PORTS=8080. EXPOSE is documentation:
# it tells Docker (and humans) which port the process listens on.
EXPOSE 8080

# Lightweight health check: probe /health every 30 s.
# Docker will mark the container unhealthy after 3 consecutive failures.
# --start-period gives the app time to start before failures count.
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "LruCache.Api.dll"]
