# LRU Cache — Complete Reference Guide

> A production-grade, thread-safe Least Recently Used cache built from scratch in C# (.NET 10),
> exposed as a REST API, structured with Clean Architecture, and shipped with a full
> Docker + GitHub Actions CI/CD pipeline.

---

## CI/CD Pipeline

- **CI status:** [![CI](https://github.com/TayyabNazeerShaikh/LruCache/actions/workflows/ci.yml/badge.svg)](https://github.com/TayyabNazeerShaikh/LruCache/actions/workflows/ci.yml)
- **CD status:** [![CD](https://github.com/TayyabNazeerShaikh/LruCache/actions/workflows/cd.yml/badge.svg)](https://github.com/TayyabNazeerShaikh/LruCache/actions/workflows/cd.yml)
- **GHCR image:** `ghcr.io/tayyabnazeersha ikh/lrucache`

---

## DevOps and CI/CD

### Pipeline architecture

```
Developer
    │
    ▼  git push / pull request
GitHub Repository (github.com/TayyabNazeerShaikh/LruCache)
    │
    ▼  on: push to main  OR  pull_request → main
┌───────────────────────────────────────────────────────┐
│  CI  (.github/workflows/ci.yml)                       │
│  ├── Restore NuGet packages (cached)                  │
│  ├── Build solution in Release                        │
│  ├── Run 16 unit tests                                │
│  ├── Run 7 integration tests                          │
│  └── Validate Docker build (no push)                  │
└───────────────────────────────────────────────────────┘
    │
    ▼  only when CI succeeds on main
┌───────────────────────────────────────────────────────┐
│  CD  (.github/workflows/cd.yml)                       │
│  ├── Build Docker image (with GHA layer cache)        │
│  ├── Tag: sha-<7-char-git-sha>  (immutable)           │
│  ├── Tag: latest                (mutable)             │
│  ├── Embed OCI labels           (source, commit, date)│
│  └── Push to GHCR                                     │
└───────────────────────────────────────────────────────┘
    │
    ▼  image available at ghcr.io/tayyabnazeersha ikh/lrucache
GitHub Container Registry (GHCR)
    │
    ▼  manual: ./scripts/deploy-local.sh sha-<commit>
┌───────────────────────────────────────────────────────┐
│  Local Deployment (scripts/deploy-local.sh/.ps1)      │
│  ├── Pull immutable image from GHCR                   │
│  ├── docker compose -f compose.deploy.yml up -d       │
│  ├── Poll GET /health until HTTP 200 (60 s max)       │
│  └── Auto-rollback to .deployed-tag on failure        │
└───────────────────────────────────────────────────────┘
    │
    ▼
Running LruCache API  (http://localhost:8080)
```

---

### Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 10.0.x | Build and test |
| Docker Desktop | 29+ | Container build and run |
| Git | any | Version control |
| curl | any | Health check probing |

**Check versions:**
```bash
dotnet --version        # should show 10.0.x
docker --version        # should show 29+
docker compose version  # should show v2.x
```

---

### Local development commands

```bash
# Restore packages
dotnet restore LruCache.slnx

# Build (Release)
dotnet build LruCache.slnx --configuration Release

# Run all tests
dotnet test LruCache.slnx --configuration Release

# Run only unit tests
dotnet test tests/LruCache.UnitTests --configuration Release

# Run only integration tests
dotnet test tests/LruCache.IntegrationTests --configuration Release

# Run the API locally (http://localhost:5073)
dotnet run --project src/LruCache.Api
```

---

### Docker commands (Phase 1)

```bash
# Build the image
docker build --tag lrucache-api:local .

# Run the container (detached, port 8080)
docker run -d --name lrucache-api -p 8080:8080 lrucache-api:local

# Inspect running containers
docker ps

# View container logs (follow)
docker logs -f lrucache-api

# Stop the container
docker stop lrucache-api

# Remove the container
docker rm lrucache-api

# Remove the image
docker rmi lrucache-api:local

# Test the health endpoint
curl http://localhost:8080/health
```

---

### Docker Compose commands (Phase 2)

`compose.yml` builds from source — use this for local development.

```bash
# Build image and start (foreground, shows logs)
docker compose up --build

# Build image and start (background / detached)
docker compose up --build -d

# View running services and their health status
docker compose ps

# View logs (follow)
docker compose logs -f api

# Stop containers (preserves volumes)
docker compose down

# Stop containers and remove all volumes
docker compose down --volumes

# Rebuild without cache (nuclear option for dependency issues)
docker compose build --no-cache
```

**First-time setup:**
```bash
cp .env.example .env
# Edit .env if you want a non-default capacity
docker compose up --build -d
curl http://localhost:8080/health    # → {"status":"Healthy"}
curl http://localhost:8080/api/cache/stats
```

---

### CI workflow explanation (Phase 3)

File: `.github/workflows/ci.yml`

**Triggers:** every push to `main` and every pull request targeting `main`.

**What it does and why:**

| Step | Command | Why |
|------|---------|-----|
| Checkout | `actions/checkout@v4` | Get the code |
| Setup .NET | `actions/setup-dotnet@v4` `10.0.x` | Exact SDK version match |
| Cache NuGet | key = `OS + hash(*.csproj)` | Skip 30 s download on repeat runs |
| Restore | `dotnet restore LruCache.slnx` | Download declared packages |
| Build | `dotnet build --configuration Release --no-restore` | Compile; fail fast on errors |
| Unit tests | `dotnet test tests/LruCache.UnitTests --no-build` | Pure data-structure correctness |
| Integration tests | `dotnet test tests/LruCache.IntegrationTests --no-build` | Full HTTP round-trips via WebApplicationFactory |
| Docker validate | `docker build --tag lrucache-api:ci-validate .` | Dockerfile builds; no push |

**Integration tests run in CI without any extra setup** because `WebApplicationFactory<Program>` boots the application in memory — no server ports, no external services, no flakiness.

---

### CD / container publishing workflow explanation (Phase 4)

File: `.github/workflows/cd.yml`

**Trigger:** `workflow_run` fires when the CI workflow **completes successfully** on `main`. PRs never trigger CD. A failed CI never triggers CD.

**What it does:**

| Step | Detail |
|------|--------|
| Checkout at tested SHA | Uses `github.event.workflow_run.head_sha` — the exact commit CI verified |
| Extract short SHA | `abc1234` → used in tag `sha-abc1234` |
| Login to GHCR | Uses `secrets.GITHUB_TOKEN` (auto-provided, no setup required) |
| Setup Buildx | Enables GHA layer cache and multi-platform support |
| Metadata | Generates tags + OCI labels (source, revision, created) |
| Build + push | `docker/build-push-action@v6` with `cache-from/cache-to: type=gha` |

**CI vs CD vs Deployment:**
- **CI** answers: *"Is the code correct?"* — runs on every change
- **CD** answers: *"Is the artifact ready?"* — runs after CI passes on main
- **Deployment** answers: *"Is the new version running in production?"* — triggered manually

---

### GHCR image naming convention

```
ghcr.io / tayyabnazeersha ikh / lrucache : sha-abc1234
   │              │                 │          │
   │              │                 │          └─ tag
   │              │                 └─ repository name (lowercase)
   │              └─ GitHub username (lowercase)
   └─ registry host
```

**Rules:**
- Image names are always lowercase (GHCR requirement)
- The repository name matches the GitHub repo name, lowercased
- Tags are controlled entirely by the CD workflow

---

### Image tagging strategy

| Tag | Example | Mutable? | Use case |
|-----|---------|----------|----------|
| `sha-<7-char>` | `sha-abc1234` | No | **Use this for deployments** — immutable, traceable to a specific commit |
| `latest` | `latest` | Yes | Documentation, quick testing — never for production deployments |

**Why immutable tags matter:**
If you deploy `latest` and something breaks, you cannot roll back — `latest` now points to the broken image. With `sha-abc1234`, you always know exactly what code is running and can deploy any previous SHA to roll back.

---

### Local deployment instructions (Phase 5)

#### Prerequisites
```bash
# Log in to GHCR (only needed once)
echo "YOUR_GITHUB_PAT" | docker login ghcr.io -u TayyabNazeerShaikh --password-stdin
```

Your GitHub PAT needs the `read:packages` scope. Create one at:
`GitHub → Settings → Developer settings → Personal access tokens`

#### Deploy a specific SHA (recommended)
```bash
# Linux / macOS / Git Bash
./scripts/deploy-local.sh sha-abc1234

# Windows PowerShell
.\scripts\deploy-local.ps1 -Tag sha-abc1234
```

#### What the script does
1. Records the currently running tag in `.deployed-tag`
2. Pulls the new image from GHCR
3. `docker compose -f compose.deploy.yml up -d` (replaces the running container)
4. Polls `GET /health` every 5 seconds for up to 60 seconds
5. On success: updates `.deployed-tag` with the new tag
6. On failure: re-pulls the previous tag and re-deploys it automatically

---

### Health-check verification

The `/health` endpoint is provided by `Microsoft.AspNetCore.Diagnostics.HealthChecks`.
It returns `200 OK` with body `{"status":"Healthy"}` when the application is ready to serve requests.

```bash
# Quick check
curl http://localhost:8080/health

# Verbose — shows headers and status code
curl -v http://localhost:8080/health

# Expected response
# HTTP/1.1 200 OK
# {"status":"Healthy"}
```

**Where health checks are used:**
- `Dockerfile` `HEALTHCHECK` instruction — Docker marks container healthy/unhealthy
- `compose.yml` / `compose.deploy.yml` `healthcheck:` — Compose health status
- `scripts/deploy-local.sh` — deployment success gate and rollback trigger

---

### Rollback procedure (Phase 7)

#### Automatic rollback (built into deploy script)

The deploy script rolls back automatically when the health check fails:

```
Version A (sha-aaa1111) deployed → healthy → .deployed-tag = "sha-aaa1111"

Deploy Version B (sha-bbb2222):
  Pull sha-bbb2222 ✓
  docker compose up -d ✓
  Health check → FAIL (12 × 5s = 60s)
  Pull sha-aaa1111 ✓
  docker compose up -d ✓
  .deployed-tag unchanged = "sha-aaa1111"
  Exit code 1 (deployment failed)
```

#### Manual rollback

```bash
# Linux / Git Bash — roll back to a known-good SHA
./scripts/deploy-local.sh sha-aaa1111

# PowerShell
.\scripts\deploy-local.ps1 -Tag sha-aaa1111
```

#### Find a previous SHA

```bash
# List recent commits with their SHAs
git log --oneline -10

# Or check the GHCR packages page on GitHub for all available tags
# https://github.com/TayyabNazeerShaikh/LruCache/pkgs/container/lrucache
```

---

### Troubleshooting common failures

#### `docker: Cannot connect to the Docker daemon`
Docker Desktop is not running. Open Docker Desktop from the Start Menu and wait for the daemon to start.

#### `git@github.com: Permission denied (publickey)`
You're using SSH auth without an SSH key. Switch to HTTPS:
```bash
git remote set-url origin https://github.com/TayyabNazeerShaikh/LruCache.git
git push -u origin main
# Enter username + GitHub PAT when prompted
```

#### `unauthorized: authentication required` (docker pull from GHCR)
Log in to GHCR first:
```bash
echo "YOUR_PAT" | docker login ghcr.io -u TayyabNazeerShaikh --password-stdin
```

#### CI fails: `dotnet restore` can't find `.slnx`
Ensure you're running commands from the repo root (where `LruCache.slnx` lives).

#### Health check fails after `docker compose up`
1. Check container logs: `docker compose logs api`
2. Confirm port: `docker compose ps`
3. Try manually: `curl -v http://localhost:8080/health`
4. Common cause: `ASPNETCORE_ENVIRONMENT` not set to `Production` → HTTPS redirect active inside container

#### CD workflow doesn't trigger
Verify CI workflow name matches exactly. In `cd.yml`:
```yaml
workflows: ["CI"]   # must match the `name:` field in ci.yml exactly
```

#### Image not visible on GHCR after CD runs
Check that the repository visibility allows package visibility. Go to:
`GitHub → Your profile → Packages → lrucache → Package settings → Change visibility`

---

### Files created by this CI/CD setup

| File | Purpose |
|------|---------|
| `Dockerfile` | Multi-stage build: SDK for compile, aspnet runtime for run |
| `.dockerignore` | Excludes `bin/`, `obj/`, tests, scripts from the build context |
| `compose.yml` | Local dev: builds image from source, starts API on port 8080 |
| `compose.deploy.yml` | Production: pulls pre-built image from GHCR via `IMAGE_TAG` env var |
| `.env.example` | Documents required environment variables without containing secrets |
| `.github/workflows/ci.yml` | CI: restore → build → test → docker validate, on push+PR to main |
| `.github/workflows/cd.yml` | CD: build + push to GHCR, only after CI passes on main |
| `scripts/deploy-local.sh` | Bash deploy script: pull → start → health check → rollback |
| `scripts/deploy-local.ps1` | PowerShell equivalent for Windows |
| `src/LruCache.Api/Program.cs` | Modified: added `/health` endpoint, guarded HTTPS redirect |

---
>
> **If you're reading this after a long break:** start at [What is an LRU Cache](#what-is-an-lru-cache),
> then jump to [Project Structure](#project-structure) to orient yourself, then read
> [How It Works Internally](#how-it-works-internally) for the core algorithm.

---

## Table of Contents

1. [What is an LRU Cache](#what-is-an-lru-cache)
2. [Why Build It From Scratch](#why-build-it-from-scratch)
3. [Project Structure](#project-structure)
4. [Clean Architecture Explained](#clean-architecture-explained)
5. [How It Works Internally](#how-it-works-internally)
6. [Thread Safety Deep Dive](#thread-safety-deep-dive)
7. [The Interface Contract](#the-interface-contract)
8. [File-by-File Walkthrough](#file-by-file-walkthrough)
9. [REST API Reference](#rest-api-reference)
10. [Dependency Injection Wiring](#dependency-injection-wiring)
11. [Running the Project](#running-the-project)
12. [Running the Tests](#running-the-tests)
13. [Time and Space Complexity](#time-and-space-complexity)
14. [Design Decisions and Trade-offs](#design-decisions-and-trade-offs)
15. [How to Extend This Project](#how-to-extend-this-project)
16. [The 10-Commit Learning Journey](#the-10-commit-learning-journey)
17. [Key C# Concepts Used](#key-c-concepts-used)
18. [Glossary](#glossary)

---

## What is an LRU Cache

A **cache** is a fast, small storage layer that holds frequently accessed data so you don't
have to fetch it from a slow source (a database, an API, disk) every time.

**LRU** stands for **Least Recently Used**. When the cache is full and a new item must be
stored, it evicts (removes) the item that was accessed *least recently* — the assumption
being that something you haven't touched in a long time is less likely to be needed again
than something you just used.

### Analogy: A physical desk

Imagine your desk has space for only 3 open books. You're working and need a 4th book. You
put away the book you haven't touched the longest (the LRU) and open the new one. That is
exactly what an LRU cache does, automatically, in memory.

### Concrete example

```
Capacity: 3

Action          Cache state (left = most recent, right = least recent)
─────────────────────────────────────────────────────────────────────
Set("A", 1)  →  [A]
Set("B", 2)  →  [B, A]
Set("C", 3)  →  [C, B, A]
Set("D", 4)  →  [D, C, B]        ← "A" was LRU, evicted
Get("B")     →  [B, D, C]        ← "B" promoted to front
Set("E", 5)  →  [E, B, D]        ← "C" was LRU, evicted
```

"B" was promoted by the `Get` call, so "C" became the new LRU and was evicted when "E"
was inserted — not "B" or "D".

---

## Why Build It From Scratch

.NET already has `IMemoryCache` and `IDistributedCache`. So why build our own?

| Aspect | `IMemoryCache` | This implementation |
|--------|---------------|---------------------|
| Eviction policy | Size-based with expiry | Strict LRU by access order |
| Access guarantees | O(1) amortized | O(1) guaranteed |
| Generics | `object` values (boxing) | `TKey`/`TValue` — fully typed |
| Recency promotion | Not guaranteed | Explicit, verifiable |
| Learning value | Zero | Everything |

Building it yourself teaches: **generics, linked list manipulation, O(1) algorithm design,
thread safety, dependency injection, and integration testing** — all in one project.

---

## Project Structure

```
LruCache/
├── src/
│   ├── LruCache.Domain/                  # Core entities (empty — reserved for future)
│   │   └── LruCache.Domain.csproj
│   │
│   ├── LruCache.Application/             # Use-case contracts (interfaces)
│   │   ├── Abstractions/
│   │   │   └── Caching/
│   │   │       └── ILruCache.cs          ← THE INTERFACE (the "what")
│   │   ├── DependencyInjection.cs
│   │   └── LruCache.Application.csproj
│   │
│   ├── LruCache.Infrastructure/          # Concrete implementations (the "how")
│   │   ├── Caching/
│   │   │   ├── LruCache.cs               ← THE ALGORITHM (Dictionary + LinkedList)
│   │   │   ├── LruCacheEntry.cs          ← Node model (stores Key + Value)
│   │   │   └── LruCacheOptions.cs        ← Strongly-typed configuration
│   │   ├── DependencyInjection.cs        ← Registers LruCache with ASP.NET Core DI
│   │   └── LruCache.Infrastructure.csproj
│   │
│   └── LruCache.Api/                     # HTTP entry point
│       ├── Controllers/
│       │   └── CacheController.cs        ← REST endpoints (GET/PUT/DELETE)
│       ├── Contracts/
│       │   └── Cache/
│       │       ├── CacheResponse.cs      ← Response DTO
│       │       └── SetCacheRequest.cs    ← Request DTO
│       ├── Program.cs                    ← App bootstrap, DI registration
│       ├── appsettings.json              ← LruCache:Capacity config
│       └── LruCache.Api.csproj
│
└── tests/
    ├── LruCache.UnitTests/               # Pure data-structure tests (no HTTP)
    │   ├── LruCacheTests.cs              ← 16 tests: behavior + concurrency
    │   └── LruCache.UnitTests.csproj
    │
    └── LruCache.IntegrationTests/        # End-to-end HTTP tests
        ├── Caching/
        │   └── CacheEndpointsTests.cs    ← 7 tests via WebApplicationFactory
        └── LruCache.IntegrationTests.csproj
```

### Dependency graph (arrows = "depends on")

```
LruCache.Api  ──────────────────────────────┐
     │                                       │
     ▼                                       ▼
LruCache.Application  ◄──────  LruCache.Infrastructure
     │
     ▼
LruCache.Domain
```

The critical rule: **inner layers never reference outer layers**. `Application` defines
the interface; `Infrastructure` implements it. `Api` wires them together at startup.

---

## Clean Architecture Explained

This project follows the **Clean Architecture** pattern (also known as Onion Architecture
or Hexagonal Architecture). The idea is to keep business logic independent of frameworks,
databases, and delivery mechanisms.

### Layer responsibilities

**Domain** — The innermost layer. Contains pure business entities and value objects.
No framework dependencies whatsoever. In this project, it is empty because the LRU
algorithm itself lives in Infrastructure, but the layer is scaffolded for future growth
(e.g., a `CacheKey` value object with validation rules).

**Application** — Defines *contracts* (interfaces) that the outer layers must satisfy.
`ILruCache<TKey, TValue>` lives here. The Application layer also contains use cases
(command/query handlers) in larger systems. It knows nothing about HTTP or databases.

**Infrastructure** — Implements the contracts defined in Application. `LruCache<TKey, TValue>`
implements `ILruCache<TKey, TValue>`. This is where the algorithm, the Dictionary, and the
LinkedList live. If you wanted to swap the implementation for a Redis-backed cache, you
would only change this layer.

**Api** — The outermost layer. Handles HTTP concerns: routing, request parsing, response
formatting. The controllers depend only on `ILruCache<TKey, TValue>` (the interface from
Application), not on any concrete class from Infrastructure.

### Why this matters in practice

```csharp
// CacheController.cs — depends on the interface, NOT the concrete class
public CacheController(ILruCache<string, string> cache) => _cache = cache;
```

Tomorrow, if you want to swap the local LRU cache for a Redis cache, you:
1. Write `RedisCacheAdapter : ILruCache<string, string>` in Infrastructure
2. Change one line in `DependencyInjection.cs`
3. `CacheController` doesn't change at all

---

## How It Works Internally

This is the most important section. Re-read this whenever you forget the algorithm.

### The O(1) problem

A cache needs three operations to be fast:

1. **Lookup**: is key X in the cache? What is its value?
2. **Promotion**: when key X is accessed, mark it as "most recently used"
3. **Eviction**: when full, remove the least recently used item

A naive list gives O(1) eviction (just remove the tail) but O(n) lookup and promotion
because you have to scan the whole list to find an item. A naive dictionary gives O(1)
lookup but O(n) promotion because you'd have to track order separately.

**The insight: combine both data structures.**

### The two data structures

```
_entries (Dictionary<TKey, LinkedListNode<LruCacheEntry<TKey, TValue>>>)

  "alice"  ───────────────────────────────────────► [node₁]
  "bob"    ───────────────────────────────────────► [node₂]
  "carol"  ───────────────────────────────────────► [node₃]


_recency (LinkedList<LruCacheEntry<TKey, TValue>>)

  HEAD ◄──────────────────────────────────────────── TAIL
  [node₃: carol,3] ↔ [node₂: bob,2] ↔ [node₁: alice,1]
       MRU                                   LRU
  (most recently used)              (evict this next)
```

The Dictionary does NOT store the value directly — it stores the **node object** that is
already inside the LinkedList. This is the key insight.

### Why this gives O(1) for everything

**TryGet (lookup + promote):**
```
1. _entries["bob"] → node₂  (O(1) hash lookup)
2. _recency.Remove(node₂)    (O(1) because we have the node, not a value)
3. _recency.AddFirst(node₂)  (O(1) prepend to head)
Total: O(1)
```

**Set (insert or update):**
```
Insert path:
1. _entries.Count >= _capacity? If yes, evict:
   a. lruNode = _recency.Last          (O(1) tail access)
   b. _entries.Remove(lruNode.Value.Key) (O(1) dictionary remove)
   c. _recency.RemoveLast()            (O(1))
2. Create new entry, _recency.AddFirst (O(1))
3. _entries[key] = newNode             (O(1))
Total: O(1)

Update path (key already exists):
1. _entries[key] → existingNode        (O(1))
2. existingNode.Value.Value = value    (O(1) in-place mutation)
3. Promote: Remove + AddFirst          (O(1))
Total: O(1)
```

**Remove:**
```
1. _entries[key] → node   (O(1))
2. _entries.Remove(key)   (O(1))
3. _recency.Remove(node)  (O(1) — we have the node reference)
Total: O(1)
```

### Why LruCacheEntry stores the Key

When we evict the tail node, we need to:
- Remove the node from `_recency` (we have the node, easy)
- Remove the corresponding entry from `_entries` (we need the KEY for this)

If we only stored the value in the node, we'd have no way to look up the dictionary key.
`LruCacheEntry<TKey, TValue>` stores **both** the key and value specifically for this case.

```csharp
// Eviction — this line requires Key to be stored in the entry
_entries.Remove(lruNode.Value.Key);   // lruNode.Value is LruCacheEntry<TKey, TValue>
```

### LinkedList vs Array

Why `LinkedList<T>` and not `List<T>` or `T[]`?

`LinkedList<T>` in .NET is a **doubly-linked list**. Each node holds a reference to the
previous and next node. This means:

- `Remove(node)` is O(1) — just update the prev/next pointers of neighbors
- `AddFirst(node)` is O(1) — update the head pointer
- `RemoveLast()` is O(1) — update the tail pointer

`List<T>` is backed by an array. `RemoveAt(0)` is O(n) because every element shifts.
`Insert(0, x)` is also O(n) for the same reason. Using `List<T>` would destroy the O(1)
guarantee that makes LRU caches useful.

---

## Thread Safety Deep Dive

### The race condition without locking

Imagine two threads calling `Set` simultaneously on a full cache:

```
Thread A:  reads _entries.Count (= 3, capacity = 3) → decides to evict
Thread B:  reads _entries.Count (= 3, capacity = 3) → decides to evict

Thread A:  evicts tail ("alice"), inserts "dave"
           _entries = {"bob", "carol", "dave"}

Thread B:  evicts tail (now "carol"), inserts "eve"
           _entries = {"bob", "dave", "eve"}
           BUT: _recency still has 3 nodes. Count = 3. Correct.

OR worse:
Thread A:  reads _recency.Last (node for "alice")
Thread B:  also reads _recency.Last (same node for "alice")
Thread A:  _recency.RemoveLast() — alice removed
Thread B:  _recency.RemoveLast() — REMOVES "carol" (new tail), not alice!
           _entries still has "alice" → phantom entry → corrupted state
```

This kind of bug is a **race condition**. It is non-deterministic — it only appears under
specific timing, making it extremely hard to reproduce or debug in production.

### The `lock` statement

```csharp
private readonly object _syncRoot = new();

public bool TryGet(TKey key, out TValue? value)
{
    lock (_syncRoot)
    {
        // Only one thread can execute this block at a time.
        // All other threads block here until the lock is released.
        ...
    }
}
```

`lock (_syncRoot)` is syntactic sugar for `Monitor.Enter` / `Monitor.Exit`. It creates a
**mutual exclusion (mutex)**: only one thread holds the lock at any moment. All other
threads block at the `lock` statement until the holder exits the block.

### Why a dedicated `_syncRoot` object?

**Never `lock(this)`** — if external code also locks on the same object for a different
reason, you get a deadlock that neither party can predict or debug.

**Never `lock(typeof(LruCache<,>))`** — type objects are shared across the entire
AppDomain; locking them can block completely unrelated code.

A private `object _syncRoot = new()` is invisible to external code. Only `LruCache<TKey, TValue>`
can acquire it — no deadlock risk from the outside.

### Why not ReaderWriterLockSlim?

`ReaderWriterLockSlim` allows multiple concurrent readers and exclusive writers. This is
useful when reads vastly outnumber writes and reads are truly read-only.

In an LRU cache, `TryGet` is **not read-only**. It promotes the node to the head of
`_recency` — that is a write operation. Because every public method writes to the
underlying data structures, `ReaderWriterLockSlim` would provide no concurrency advantage
over a plain `lock`, while adding complexity and overhead.

### The `Count` property

```csharp
public int Count { get { lock (_syncRoot) return _entries.Count; } }
```

`Count` also acquires the lock. Without it, a thread could read `Count` mid-operation and
see a temporarily inconsistent value (e.g., after the tail is evicted but before the new
entry is inserted, `Count` would be `_capacity - 1` for a brief moment).

---

## The Interface Contract

```csharp
// src/LruCache.Application/Abstractions/Caching/ILruCache.cs

public interface ILruCache<TKey, TValue> where TKey : notnull
{
    int Count { get; }
    int Capacity { get; }
    bool TryGet(TKey key, out TValue? value);
    void Set(TKey key, TValue value);
    bool Remove(TKey key);
    void Clear();
}
```

### Generic constraints: `where TKey : notnull`

`Dictionary<TKey, TValue>` requires non-null keys. Without the constraint, a caller could
pass `null` as `TKey` (if it were a reference type) and get a `NullReferenceException`
at runtime inside the Dictionary. The constraint `where TKey : notnull` makes this a
**compile-time error** instead — the bug is caught before the code even runs.

### The `bool/out` pattern for `TryGet`

```csharp
bool TryGet(TKey key, out TValue? value);
```

This is the standard .NET pattern for operations that may or may not find a result
(see also: `Dictionary.TryGetValue`, `int.TryParse`, `Queue.TryDequeue`). It avoids
two alternatives that are both worse:

- **Return null on miss** — doesn't work when `TValue` is a value type (`int`, `struct`),
  and forces null-checks on every call even when the caller knows the key exists
- **Throw on miss** — exceptions are expensive and wrong for "not found" which is
  an expected, normal condition, not an error

---

## File-by-File Walkthrough

### `ILruCache.cs` — The contract
**Location:** `src/LruCache.Application/Abstractions/Caching/ILruCache.cs`

The interface that all consumers depend on. Defines six members: two read properties
(`Count`, `Capacity`) and four operations (`TryGet`, `Set`, `Remove`, `Clear`).
Lives in Application so Infrastructure and Api both reference it without creating
a circular dependency.

---

### `LruCacheEntry.cs` — The node payload
**Location:** `src/LruCache.Infrastructure/Caching/LruCacheEntry.cs`

```csharp
internal sealed class LruCacheEntry<TKey, TValue>
{
    internal TKey Key { get; }       // immutable — the key never changes
    internal TValue Value { get; set; } // mutable — Set() can update in-place
    ...
}
```

This is what each `LinkedListNode` carries. `Key` is read-only — once a node is
created for a key, it never moves to a different key. `Value` is mutable so
`Set(existingKey, newValue)` can update it without creating a new node.

`internal sealed` — internal because this is an implementation detail. External code
(Api, tests via the interface) never needs to know this type exists.

---

### `LruCacheOptions.cs` — Configuration
**Location:** `src/LruCache.Infrastructure/Caching/LruCacheOptions.cs`

```csharp
public sealed class LruCacheOptions
{
    public const int DefaultCapacity = 100;
    public int Capacity { get; set; } = DefaultCapacity;
}
```

Bound to the `"LruCache"` section of `appsettings.json`:
```json
{ "LruCache": { "Capacity": 100 } }
```

Change capacity by updating `appsettings.json` — no code changes needed.

---

### `LruCache.cs` — The algorithm
**Location:** `src/LruCache.Infrastructure/Caching/LruCache.cs`

The heart of the project. Two constructors:

```csharp
// Used by ASP.NET Core DI — reads Capacity from IOptions<LruCacheOptions>
public LruCache(IOptions<LruCacheOptions> options) : this(options.Value.Capacity) { }

// Used by unit tests directly — clean and simple
internal LruCache(int capacity) { ... }
```

The `internal` constructor lets unit tests construct the cache without going through
DI, while the `public` IOptions constructor is the entry point used in production.
`InternalsVisibleTo("LruCache.UnitTests")` in the csproj makes the internal constructor
visible to the test project.

---

### `DependencyInjection.cs` (Infrastructure) — Registration
**Location:** `src/LruCache.Infrastructure/DependencyInjection.cs`

```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services)
{
    services.TryAddSingleton<ILruCache<string, string>, LruCache<string, string>>();
    return services;
}
```

`TryAddSingleton` — registers only if not already registered. This is important for
integration tests: `WebApplicationFactory` may register its own overrides, and
`TryAdd` prevents double-registration from silently winning.

`Singleton` — one shared instance for the lifetime of the application. All HTTP requests
share the same cache. This is correct for a cache (sharing is the whole point) and is
why thread safety is mandatory.

---

### `CacheController.cs` — REST endpoints
**Location:** `src/LruCache.Api/Controllers/CacheController.cs`

```csharp
[ApiController]
[Route("api/cache")]
public sealed class CacheController : ControllerBase
{
    private readonly ILruCache<string, string> _cache;
    public CacheController(ILruCache<string, string> cache) => _cache = cache;
    ...
}
```

The controller asks for `ILruCache<string, string>` — the **interface**, not the class.
The DI container injects the singleton `LruCache<string, string>` instance automatically.
If the Infrastructure registration is changed to a Redis adapter, this file doesn't change.

---

### `Program.cs` — Application bootstrap
**Location:** `src/LruCache.Api/Program.cs`

```csharp
builder.Services.Configure<LruCacheOptions>(
    builder.Configuration.GetSection("LruCache"));   // bind appsettings.json

builder.Services.AddInfrastructure();               // register LruCache singleton
```

The `public partial class Program { }` at the bottom is required by
`WebApplicationFactory<Program>` in integration tests — it makes `Program` accessible
from the test assembly.

---

## REST API Reference

Base URL (development): `https://localhost:7xxx/api/cache`

### GET `/api/cache/{key}`

Retrieves a value by key. **Promotes the entry to MRU** (this is a side-effect of GET
in an LRU cache — it counts as an access).

**Response 200 OK:**
```json
{ "key": "greeting", "value": "hello" }
```

**Response 404 Not Found** — key does not exist in the cache.

---

### PUT `/api/cache/{key}`

Inserts or updates a value. If the cache is at capacity and the key is new, the LRU
entry is evicted first.

**Request body:**
```json
{ "value": "hello world" }
```

**Response 204 No Content** — always succeeds (no body).

---

### DELETE `/api/cache/{key}`

Explicitly removes a single entry.

**Response 204 No Content** — entry existed and was removed.

**Response 404 Not Found** — key did not exist.

---

### DELETE `/api/cache`

Clears all entries. Equivalent to calling `Remove` on every key.

**Response 204 No Content** — always succeeds.

---

### GET `/api/cache/stats`

Returns current cache state. Useful for monitoring.

**Response 200 OK:**
```json
{ "count": 42, "capacity": 100 }
```

`count` is the number of entries currently stored. `capacity` is the configured maximum
(from `appsettings.json`).

---

## Dependency Injection Wiring

This section traces the full DI chain from `appsettings.json` to `CacheController`.

```
appsettings.json
  "LruCache": { "Capacity": 100 }
        │
        │  builder.Services.Configure<LruCacheOptions>(
        │      builder.Configuration.GetSection("LruCache"))
        ▼
  IOptions<LruCacheOptions>  (registered by the Configure call above)
        │
        │  services.TryAddSingleton<ILruCache<string,string>,
        │                           LruCache<string,string>>()
        ▼
  LruCache<string, string>  (constructor: public LruCache(IOptions<LruCacheOptions>))
        │
        │  DI resolves ILruCache<string,string> for CacheController
        ▼
  CacheController(ILruCache<string, string> cache)
```

**Key point about Singleton lifetime:** When the DI container resolves
`ILruCache<string, string>` for the first time, it constructs one `LruCache<string, string>`
instance and stores it. For every subsequent HTTP request, it injects the **same instance**.
This is intentional — the cache is shared across all requests. It is also why the `lock`
in Step 9 is non-negotiable.

---

## Running the Project

### Prerequisites

- .NET 10 SDK ([dotnet.microsoft.com/download](https://dotnet.microsoft.com/download))
- Git

### Clone and run

```bash
git clone https://github.com/TayyabNazeerShaikh/LruCache.git
cd LruCache
dotnet run --project src/LruCache.Api
```

The API starts on `https://localhost:7xxx`. Open the OpenAPI docs at
`https://localhost:7xxx/openapi/v1.json` (development only).

### Try it with curl

```bash
# Store a value
curl -X PUT https://localhost:7xxx/api/cache/name \
     -H "Content-Type: application/json" \
     -d '{"value": "Tayyab"}' -k

# Retrieve it
curl https://localhost:7xxx/api/cache/name -k

# Check stats
curl https://localhost:7xxx/api/cache/stats -k

# Delete it
curl -X DELETE https://localhost:7xxx/api/cache/name -k
```

### Change the capacity

Edit `src/LruCache.Api/appsettings.json`:
```json
{
  "LruCache": {
    "Capacity": 500
  }
}
```

Restart the app. The new capacity takes effect immediately.

---

## Running the Tests

### All tests

```bash
dotnet test
```

Expected output:
```
Passed!  - Failed: 0, Passed: 16   ← unit tests
Passed!  - Failed: 0, Passed:  7   ← integration tests
```

### Unit tests only

```bash
dotnet test tests/LruCache.UnitTests
```

These test the data structure directly — no HTTP, no DI. They construct
`LruCache<string, int>` (or `int, int`) using the `internal` constructor and
assert LRU behavior: eviction order, promotion, count consistency, edge cases.

### Integration tests only

```bash
dotnet test tests/LruCache.IntegrationTests
```

These boot the full ASP.NET Core app in memory via `WebApplicationFactory<Program>`.
Every test makes real HTTP calls and asserts on real HTTP response codes and bodies.
They catch bugs that unit tests miss: routing errors, wrong status codes, DI
misconfiguration, JSON serialization issues.

### Test categories

**Unit tests (`LruCacheTests.cs`)**
| Test | What it verifies |
|------|-----------------|
| `TryGet_MissingKey_ReturnsFalse` | Cache miss returns false, out-param is default |
| `TryGet_ExistingKey_ReturnsTrueAndValue` | Cache hit returns correct value |
| `Set_NewKey_IncreasesCount` | Count tracks inserts correctly |
| `Set_ExistingKey_UpdatesValueWithoutIncreasingCount` | Update is not an insert |
| `Set_BeyondCapacity_EvictsLeastRecentlyUsed` | Core eviction behavior |
| `TryGet_PromotesEntry_SoItSurvivesEviction` | Promotion prevents eviction |
| `Set_UpdateExistingKey_PromotesItSoItSurvivesEviction` | Update also promotes |
| `Remove_ExistingKey_ReturnsTrueAndDecreasesCount` | Explicit eviction |
| `Remove_MissingKey_ReturnsFalse` | No error on missing key |
| `Remove_ThenSet_SlotIsReusable` | Freed capacity is reused |
| `Clear_RemovesAllEntriesAndResetsCount` | Full wipe |
| `Clear_ThenSet_CacheIsFullyUsable` | Cache works after clear |
| `Constructor_ZeroCapacity_ThrowsArgumentOutOfRangeException` | Invalid input guard |
| `Capacity_AlwaysReturnsValueFromConstructor` | Property is correct |
| `ConcurrentSets_NeverExceedCapacity` | Thread safety: count invariant |
| `ConcurrentMixedOperations_DoNotThrow` | Thread safety: no corruption/deadlock |

**Integration tests (`CacheEndpointsTests.cs`)**
| Test | What it verifies |
|------|-----------------|
| `Get_NonExistentKey_Returns404` | Correct 404 on miss |
| `Put_ThenGet_ReturnsStoredValue` | Full round-trip via HTTP |
| `Put_Twice_OverwritesValue` | Update via HTTP |
| `Delete_ExistingKey_Returns204_ThenGet_Returns404` | Delete then verify gone |
| `Delete_NonExistentKey_Returns404` | Correct 404 on delete miss |
| `DeleteAll_ClearsCache` | Clear via HTTP |
| `Stats_ReturnsCountAndCapacity` | Stats endpoint format |

---

## Time and Space Complexity

### Time complexity

| Operation | Time | Why |
|-----------|------|-----|
| `TryGet` (hit) | O(1) | Dictionary lookup + two linked list pointer updates |
| `TryGet` (miss) | O(1) | Dictionary lookup only |
| `Set` (new key, under capacity) | O(1) | Dictionary insert + LinkedList.AddFirst |
| `Set` (new key, at capacity) | O(1) | Tail removal (O(1)) + above |
| `Set` (existing key) | O(1) | In-place value update + promotion |
| `Remove` | O(1) | Dictionary lookup + both structure removals |
| `Clear` | O(n) | Must clear n entries from both structures |
| `Count` | O(1) | Dictionary.Count property |

All hot-path operations (`TryGet`, `Set`, `Remove`) are **O(1)**. This is the entire
point of the Dictionary + LinkedList design. A naive ordered-list approach would be O(n)
for every operation.

### Space complexity

| Component | Space |
|-----------|-------|
| `_entries` (Dictionary) | O(n) — n = current entry count |
| `_recency` (LinkedList) | O(n) — one node per entry |
| Each `LinkedListNode` | O(1) — prev pointer, next pointer, value |
| Each `LruCacheEntry` | O(1) — key + value |
| **Total** | **O(capacity)** — bounded by capacity |

The pre-sized Dictionary (`new Dictionary<TKey, ...>(capacity)`) allocates the internal
hash bucket array upfront. This avoids runtime rehashing under load and keeps
`Set` truly O(1) (amortized O(1) without pre-sizing due to resize events).

---

## Design Decisions and Trade-offs

### 1. `string` key and value in the API controller

`CacheController` is typed to `ILruCache<string, string>`. This is a pragmatic choice
for a REST API — all HTTP data arrives as text. If you need typed values (integers,
objects), serialize them to JSON strings before storing, or create a separate controller
and DI registration for each type you need.

### 2. `lock` vs `ReaderWriterLockSlim`

Addressed in the Thread Safety section. Short answer: every LRU operation writes
(even TryGet promotes), so read/write lock separation offers no benefit.

### 3. `TryAddSingleton` vs `AddSingleton`

`TryAddSingleton` registers only if the service is not already registered.
`WebApplicationFactory` in integration tests may want to swap the cache with a
test double. `TryAdd` allows tests to register a mock first, preventing the real
implementation from overwriting it.

### 4. Two constructors

```csharp
public LruCache(IOptions<LruCacheOptions> options) : this(options.Value.Capacity) { }
internal LruCache(int capacity) { ... }
```

The public IOptions constructor is for DI in production. The internal int constructor
is for unit tests — it's simpler to write `new LruCache<string,int>(3)` in a test than
to construct an `IOptions<LruCacheOptions>` mock. They delegate to the same logic so
there is no code duplication.

### 5. Eviction on `Set`, not on a background thread

Eviction happens synchronously inside `Set`. An alternative is a background sweeper
that evicts on a timer. Synchronous eviction is simpler, deterministic, and avoids
the complexity of a background thread and the locking it would require.

### 6. No TTL (time-to-live)

This implementation does not expire entries based on time. Adding TTL would require:
- Storing an expiry timestamp in `LruCacheEntry`
- A background thread or lazy expiry check on access
- Additional lock contention

TTL is a separate eviction policy on top of LRU. It is a natural next extension
(see "How to Extend This Project" below).

---

## How to Extend This Project

### Add TTL (time-to-live) expiry

1. Add `ExpiresAt` property to `LruCacheEntry<TKey, TValue>`
2. In `TryGet`, check `entry.ExpiresAt < DateTimeOffset.UtcNow` — if expired, remove and return false
3. Add `TimeSpan? absoluteExpiry` parameter to `Set`
4. Add `SlidingExpiry` option to `LruCacheOptions`

### Support multiple cache types (open generic registration)

Change the DI registration to:
```csharp
services.TryAddSingleton(typeof(ILruCache<,>), typeof(LruCache<,>));
```

Now `ILruCache<int, User>`, `ILruCache<Guid, Order>` etc. all resolve automatically.

### Add cache statistics

Add a `CacheStatistics` class tracking: total hits, total misses, total evictions,
hit rate. Increment counters inside `TryGet` and `Set`. Expose via the `/stats` endpoint.

### Replace `lock` with `System.Collections.Concurrent` structures

For even higher throughput under read-heavy workloads, consider
`ConcurrentDictionary` + a lock-free list (though true lock-free LRU is notoriously
difficult to implement correctly). The current `lock` approach is correct and simple;
optimize only if profiling shows the lock is a bottleneck.

### Add a cache warming endpoint

```csharp
[HttpPost("warm")]
public IActionResult Warm([FromBody] Dictionary<string, string> entries) { ... }
```

Bulk-insert entries on application startup from a persistent store.

---

## The 10-Commit Learning Journey

This project was built commit-by-commit as a learning exercise. Here is what each
commit taught, in order:

| # | Commit message | Concept learned |
|---|---------------|-----------------|
| 1 | `feat: initialize solution structure with clean architecture layers` | Clean Architecture, 4-layer rule, dependency direction |
| 2 | `feat: define ILruCache<TKey,TValue> contract in Application layer` | Generics, `where TKey : notnull`, interface-driven design |
| 3 | `feat: add LruCacheEntry<TKey,TValue> node model and LruCacheOptions` | Why the node must store the key; typed config vs magic numbers |
| 4 | `feat: scaffold LruCache with Dictionary+LinkedList fields and constructor` | The O(1) insight: Dictionary maps key→node, LinkedList maintains order |
| 5 | `feat: implement TryGet with O(1) recency promotion` | Promotion = Remove(node) + AddFirst(node); O(1) because we hold the node |
| 6 | `feat: implement Set with O(1) insertion and LRU eviction` | Insert at head; evict tail; why the key is needed for eviction |
| 7 | `feat: implement Remove and Clear completing the non-thread-safe LRU cache` | Both structures must always stay in sync; no partial updates |
| 8 | `feat: add 14 unit tests covering LRU cache behavior (all passing)` | xUnit, AAA pattern, testing LRU-specific invariants not just method calls |
| 9 | `feat: make LruCache thread-safe with lock and add concurrency tests` | Race conditions, mutual exclusion, `_syncRoot`, why RWLS doesn't help here |
| 10 | `feat: register cache with DI, expose REST API, add 7 integration tests` | `IOptions<T>`, Singleton lifetime, `WebApplicationFactory`, end-to-end testing |

To see exactly what changed in any commit:
```bash
git show <commit-hash>
git log --oneline   # to get the hashes
```

---

## Key C# Concepts Used

### Generics
```csharp
public interface ILruCache<TKey, TValue> where TKey : notnull
```
One definition, works for any type pair. `where TKey : notnull` is a generic constraint
that prevents `null` keys at compile time.

### `sealed` classes
```csharp
internal sealed class LruCache<TKey, TValue> : ILruCache<TKey, TValue>
```
`sealed` prevents inheritance. LRU behavior is an invariant — subclasses could break it
by overriding methods. `sealed` also allows the JIT compiler to devirtualize method calls.

### `out` parameters
```csharp
bool TryGet(TKey key, out TValue? value);
```
`out` parameters must be assigned by the callee before the method returns. The `?` makes
`TValue?` nullable — the value is `default` on a miss.

### Extension methods
```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services)
```
The `this` keyword on the first parameter makes `AddInfrastructure` callable as
`services.AddInfrastructure()` — as if it were a method on `IServiceCollection`.
This is how all ASP.NET Core `Add*` methods are built.

### Records
```csharp
public sealed record CacheResponse(string Key, string? Value);
public sealed record SetCacheRequest(string Value);
```
Records are immutable value-type-like classes. One line generates a constructor,
properties, `Equals`, `GetHashCode`, and `ToString`. Perfect for DTOs.

### `IOptions<T>`
```csharp
public LruCache(IOptions<LruCacheOptions> options) : this(options.Value.Capacity) { }
```
ASP.NET Core's standard pattern for injecting configuration. The DI container resolves
`IOptions<LruCacheOptions>` using the `Configure<T>` registration in `Program.cs`.

### `partial class`
```csharp
public partial class Program { }
```
Marks the compiler-generated `Program` class as partial so `WebApplicationFactory<Program>`
in the test assembly can reference it. Without this, the test project cannot discover the
entry point of the application.

---

## Glossary

**Cache** — A fast, small storage layer between a consumer and a slow data source.

**Capacity** — The maximum number of entries the cache will hold before eviction begins.

**Doubly-linked list** — A list where each node has a pointer to both the next and
previous node, enabling O(1) removal of any node when you hold a reference to it.

**Eviction** — Removing an entry from the cache to make room for a new one.

**InternalsVisibleTo** — An assembly attribute that grants a named assembly access to
`internal` members. Used here to let `LruCache.UnitTests` access `LruCache<TKey,TValue>`.

**LRU (Least Recently Used)** — An eviction policy that removes the item accessed least
recently when the cache is full.

**MRU (Most Recently Used)** — The item accessed most recently; the head of `_recency`.

**Mutex (Mutual Exclusion)** — A synchronization primitive that ensures only one thread
executes a critical section at a time.

**Promotion** — Moving an accessed cache entry to the MRU position (head of the list).

**Race condition** — A bug where the outcome depends on the relative timing of two or
more threads, making it non-deterministic and hard to reproduce.

**Recency** — How recently an item was accessed. "High recency" = accessed very recently.

**Singleton** — A DI lifetime meaning one shared instance is created and reused for
the entire application lifetime.

**TryGet pattern** — A .NET convention: `bool TryXxx(in, out result)` returns false
(not an exception) when the result is not available.

**WebApplicationFactory** — An ASP.NET Core test utility that hosts the real application
in memory, allowing integration tests to make HTTP calls without a running server.
