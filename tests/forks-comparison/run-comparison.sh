#!/bin/bash
# Readarr Forks Comparison Test Harness
# Tests multiple Readarr fork images against a real audiobook library.
# Docker images get READ-ONLY access to the media dirs.
#
# Requirements: docker compose, jq, curl, bc
# Run on a node with MooseFS mounted

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AUDIOBOOKS_DIR="/mnt/moosefs/media/audiobooks"
EBOOKS_DIR="/mnt/moosefs/media/ebooks"
RESULTS_DIR="${SCRIPT_DIR}/results"
COMPOSE_FILE="${SCRIPT_DIR}/docker-compose.forks-test.yml"
NETWORK_NAME="forks-test-net"

# Forks to test (name -> image)
declare -A FORK_IMAGES
FORK_IMAGES[aleksclark]="ghcr.io/aleksclark/readarr:develop"
FORK_IMAGES[bookshelf]="ghcr.io/pennydreadful/bookshelf:nightly"
FORK_IMAGES[faustvii]="ghcr.io/faustvii/readarr:nightly"
FORK_IMAGES[linuxserver]="lscr.io/linuxserver/readarr:develop"

# Port allocation (forkname -> host port)
declare -A FORK_PORTS
FORK_PORTS[aleksclark]=18780
FORK_PORTS[bookshelf]=18781
FORK_PORTS[faustvii]=18782
FORK_PORTS[linuxserver]=18783

cleanup() {
  echo ""
  echo "Cleaning up containers..."
  docker compose -f "$COMPOSE_FILE" down -v --remove-orphans 2>/dev/null || true
}

mkdir -p "$RESULTS_DIR"

# ─── Generate docker-compose ─────────────────────────────────────────────────

generate_compose() {
  cat > "$COMPOSE_FILE" <<EOF
services:
EOF

  for fork_name in "${!FORK_IMAGES[@]}"; do
    local image="${FORK_IMAGES[$fork_name]}"
    local port="${FORK_PORTS[$fork_name]}"
    local db_name="readarr_${fork_name}"

    cat >> "$COMPOSE_FILE" <<EOF

  db-${fork_name}:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: readarr
      POSTGRES_PASSWORD: readarr
      POSTGRES_DB: ${db_name}
    tmpfs:
      - /var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U readarr"]
      interval: 5s
      timeout: 3s
      retries: 10
    restart: "no"

  ${fork_name}:
    image: ${image}
    ports:
      - "${port}:8787"
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/Chicago
    volumes:
      - ${fork_name}-config:/config
      - ${AUDIOBOOKS_DIR}:/audiobooks:ro
      - ${EBOOKS_DIR}:/ebooks:ro
    depends_on:
      db-${fork_name}:
        condition: service_healthy
    restart: "no"

EOF
  done

  # Volumes section
  echo "volumes:" >> "$COMPOSE_FILE"
  for fork_name in "${!FORK_IMAGES[@]}"; do
    echo "  ${fork_name}-config:" >> "$COMPOSE_FILE"
  done
}

# ─── Wait for a fork to be ready ────────────────────────────────────────────

wait_for_fork() {
  local name="$1" port="$2" timeout="${3:-300}"
  local start=$(date +%s)
  echo -n "  Waiting for ${name}:${port}..."
  while true; do
    local code=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:${port}/ping" 2>/dev/null || echo "000")
    if [ "$code" = "200" ] || [ "$code" = "401" ]; then
      echo " ready (HTTP ${code})"
      return 0
    fi
    local elapsed=$(( $(date +%s) - start ))
    if [ $elapsed -ge $timeout ]; then
      echo " TIMEOUT (last: HTTP ${code})"
      return 1
    fi
    sleep 5
    echo -n "."
  done
}

# ─── Get API key from config ────────────────────────────────────────────────

get_api_key() {
  local name="$1" port="$2"
  # First try without auth (some forks start with auth disabled)
  local resp=$(curl -sf "http://localhost:${port}/initialize.json" 2>/dev/null || echo "{}")
  local key=$(echo "$resp" | jq -r '.apiKey // empty' 2>/dev/null)
  if [ -n "$key" ]; then
    echo "$key"
    return 0
  fi

  # Try reading from the container's config.xml
  local container_name=$(docker compose -f "$COMPOSE_FILE" ps --format json 2>/dev/null | jq -rs ".[] | select(.Name | contains(\"${name}\")) | select(.Name | contains(\"db\") | not) | .Name" 2>/dev/null || echo "")
  if [ -n "$container_name" ]; then
    key=$(docker exec "$container_name" cat /config/config.xml 2>/dev/null | grep -oP '<ApiKey>\K[^<]+' 2>/dev/null || echo "")
    if [ -n "$key" ]; then
      echo "$key"
      return 0
    fi
  fi

  echo ""
}

# ─── Configure instance ─────────────────────────────────────────────────────

configure_instance() {
  local name="$1" port="$2" api_key="$3"
  local base_url="http://localhost:${port}"
  local auth_header=""
  [ -n "$api_key" ] && auth_header="X-Api-Key: ${api_key}"

  echo "  Configuring ${name}..."

  # Add audiobooks root folder
  local rf_resp=$(curl -sf "${base_url}/api/v1/rootfolder" \
    -X POST \
    -H "Content-Type: application/json" \
    ${auth_header:+-H "$auth_header"} \
    -d '{
      "path": "/audiobooks",
      "name": "Audiobooks",
      "defaultMetadataProfileId": 1,
      "defaultQualityProfileId": 1,
      "defaultMonitorOption": "all",
      "isCalibreLibrary": false
    }' 2>/dev/null || echo "FAIL")

  if [ "$rf_resp" = "FAIL" ]; then
    echo "    WARNING: Could not add audiobooks root folder"
  else
    echo "    Added /audiobooks root folder"
  fi

  # Add ebooks root folder
  rf_resp=$(curl -sf "${base_url}/api/v1/rootfolder" \
    -X POST \
    -H "Content-Type: application/json" \
    ${auth_header:+-H "$auth_header"} \
    -d '{
      "path": "/ebooks",
      "name": "eBooks",
      "defaultMetadataProfileId": 1,
      "defaultQualityProfileId": 1,
      "defaultMonitorOption": "all",
      "isCalibreLibrary": false
    }' 2>/dev/null || echo "FAIL")

  if [ "$rf_resp" != "FAIL" ]; then
    echo "    Added /ebooks root folder"
  fi
}

# ─── Trigger library scan ───────────────────────────────────────────────────

trigger_scan() {
  local name="$1" port="$2" api_key="$3"
  local base_url="http://localhost:${port}"
  local auth_header=""
  [ -n "$api_key" ] && auth_header="X-Api-Key: ${api_key}"

  echo "  Triggering scan for ${name}..."

  # Try different command names (varies by fork)
  for cmd in "RescanFolders" "RefreshAuthor" "ApplicationCheckUpdate"; do
    curl -sf "${base_url}/api/v1/command" \
      -X POST \
      -H "Content-Type: application/json" \
      ${auth_header:+-H "$auth_header"} \
      -d "{\"name\":\"${cmd}\"}" >/dev/null 2>&1 || true
  done
}

# ─── Wait for tasks to finish ────────────────────────────────────────────────

wait_for_tasks() {
  local name="$1" port="$2" api_key="$3" timeout="${4:-600}"
  local base_url="http://localhost:${port}"
  local auth_header=""
  [ -n "$api_key" ] && auth_header="X-Api-Key: ${api_key}"
  local start=$(date +%s)

  echo -n "  Waiting for ${name} tasks..."
  sleep 30
  while true; do
    local queue=$(curl -sf "${base_url}/api/v1/command" \
      ${auth_header:+-H "$auth_header"} 2>/dev/null \
      | jq '[.[] | select(.status == "started" or .status == "queued")] | length' 2>/dev/null || echo "0")

    if [ "$queue" = "0" ] || [ "$queue" = "" ]; then
      echo " done!"
      return 0
    fi
    local elapsed=$(( $(date +%s) - start ))
    if [ $elapsed -ge $timeout ]; then
      echo " timeout (${queue} tasks remaining)"
      return 0
    fi
    sleep 15
    echo -n "."
  done
}

# ─── Collect statistics ──────────────────────────────────────────────────────

collect_stats() {
  local name="$1" port="$2" api_key="$3" output_file="$4"
  local base_url="http://localhost:${port}"
  local auth_header=""
  [ -n "$api_key" ] && auth_header="X-Api-Key: ${api_key}"

  echo "  Collecting stats for ${name}..."

  local authors=$(curl -sf "${base_url}/api/v1/author" ${auth_header:+-H "$auth_header"} 2>/dev/null || echo "[]")
  local books=$(curl -sf "${base_url}/api/v1/book" ${auth_header:+-H "$auth_header"} 2>/dev/null || echo "[]")
  local bookfiles=$(curl -sf "${base_url}/api/v1/bookfile" ${auth_header:+-H "$auth_header"} 2>/dev/null || echo "[]")

  local author_count=$(echo "$authors" | jq 'length' 2>/dev/null || echo 0)
  local book_count=$(echo "$books" | jq 'length' 2>/dev/null || echo 0)
  local bookfile_count=$(echo "$bookfiles" | jq 'length' 2>/dev/null || echo 0)

  # Get disk space stats from the status endpoint
  local wanted=$(echo "$books" | jq '[.[] | select(.monitored == true and .statistics.bookFileCount == 0)] | length' 2>/dev/null || echo 0)
  local have=$(echo "$books" | jq '[.[] | select(.statistics.bookFileCount > 0)] | length' 2>/dev/null || echo 0)

  cat > "$output_file" <<EOF
{
  "fork": "${name}",
  "image": "${FORK_IMAGES[$name]}",
  "authors_found": ${author_count},
  "books_total": ${book_count},
  "books_with_files": ${have},
  "books_missing": ${wanted},
  "total_book_files": ${bookfile_count},
  "collected_at": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
EOF

  echo "    Authors: ${author_count} | Books (total): ${book_count} | Books w/ files: ${have} | Book files: ${bookfile_count}"
}

# ─── Generate FORKS.md ───────────────────────────────────────────────────────

generate_report() {
  local output="${RESULTS_DIR}/FORKS.md"
  local test_date=$(date -u +%Y-%m-%d)

  cat > "$output" <<EOF
# Readarr Forks Comparison

**Test Date:** ${test_date}
**Audiobook Library:** 52 authors, ~16,234 audio files
**eBook Library:** 6 authors
**Host:** $(hostname) (MooseFS)
**Method:** Fresh DB per fork, media dirs mounted read-only, wait for auto-import

## Results — Audio Library

| Fork | Authors | Books (total) | Books w/ Files | Book Files | Image |
|------|---------|---------------|----------------|------------|-------|
EOF

  for fork_name in aleksclark bookshelf faustvii linuxserver; do
    local result_file="${RESULTS_DIR}/${fork_name}.json"
    [ -f "$result_file" ] || continue
    local authors=$(jq -r '.authors_found' "$result_file")
    local books=$(jq -r '.books_total' "$result_file")
    local have=$(jq -r '.books_with_files' "$result_file")
    local files=$(jq -r '.total_book_files' "$result_file")
    local image=$(jq -r '.image' "$result_file")

    echo "| ${fork_name} | ${authors} | ${books} | ${have} | ${files} | \`${image}\` |" >> "$output"
  done

  cat >> "$output" <<'EOF'

## Methodology

1. Start each fork with a completely fresh PostgreSQL database (tmpfs — no persistence)
2. Mount audiobooks library **read-only** at `/audiobooks`
3. Mount ebooks library **read-only** at `/ebooks`
4. Add root folders with default quality/metadata profiles
5. Wait for all import/scan tasks to complete (up to 10 min per fork)
6. Query API for final author/book/file counts

### Key Metrics

- **Authors:** Number of authors the fork identified from the library structure
- **Books (total):** Total books in the database (from metadata lookups)
- **Books w/ Files:** Books that have at least one matched file on disk
- **Book Files:** Total number of audio/ebook files successfully linked to a book

### Notes

- `aleksclark` fork uses Open Library for metadata
- `bookshelf` and `faustvii` use reading-glasses (api.bookinfo.pro / GoodReads data)
- `linuxserver` uses the original Readarr metadata service (partially defunct)
- All Docker images have **read-only** access to media directories
- Each fork starts with an isolated, empty database

## Reproduction

```bash
# On a node with MooseFS mounted at /mnt/moosefs
cd tests/forks-comparison
./run-comparison.sh
```

Requirements: `docker compose`, `jq`, `curl`, `bc`.
EOF

  echo "Report written to: ${output}"
}

# ─────────────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────────────

echo "╔══════════════════════════════════════════════════════════════╗"
echo "║  Readarr Forks Comparison Test                               ║"
echo "║  Audio: ${AUDIOBOOKS_DIR} (52 authors)"
echo "║  eBook: ${EBOOKS_DIR} (6 authors)"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""

if [ ! -d "$AUDIOBOOKS_DIR" ]; then
  echo "ERROR: Audiobooks directory not found: $AUDIOBOOKS_DIR"
  exit 1
fi

echo "1. Generating docker-compose..."
generate_compose
echo "   Created: ${COMPOSE_FILE}"

echo ""
echo "2. Pulling images..."
docker compose -f "$COMPOSE_FILE" pull 2>&1 | tail -5

echo ""
echo "3. Starting all fork instances..."
docker compose -f "$COMPOSE_FILE" up -d 2>&1

echo ""
echo "4. Waiting for instances to become ready..."
for fork_name in aleksclark bookshelf faustvii linuxserver; do
  wait_for_fork "$fork_name" "${FORK_PORTS[$fork_name]}" 300 || true
done

echo ""
echo "5. Getting API keys and configuring..."
declare -A API_KEYS
for fork_name in aleksclark bookshelf faustvii linuxserver; do
  key=$(get_api_key "$fork_name" "${FORK_PORTS[$fork_name]}")
  API_KEYS[$fork_name]="$key"
  [ -n "$key" ] && echo "  ${fork_name}: API key found" || echo "  ${fork_name}: no auth needed"
  configure_instance "$fork_name" "${FORK_PORTS[$fork_name]}" "$key" || true
done

echo ""
echo "6. Triggering library scans..."
for fork_name in aleksclark bookshelf faustvii linuxserver; do
  trigger_scan "$fork_name" "${FORK_PORTS[$fork_name]}" "${API_KEYS[$fork_name]}" || true
done

echo ""
echo "7. Waiting for imports (up to 10 min per fork)..."
for fork_name in aleksclark bookshelf faustvii linuxserver; do
  wait_for_tasks "$fork_name" "${FORK_PORTS[$fork_name]}" "${API_KEYS[$fork_name]}" 600 || true
done

echo ""
echo "8. Collecting results..."
for fork_name in aleksclark bookshelf faustvii linuxserver; do
  collect_stats "$fork_name" "${FORK_PORTS[$fork_name]}" "${API_KEYS[$fork_name]}" "${RESULTS_DIR}/${fork_name}.json" || true
done

echo ""
echo "9. Generating comparison report..."
generate_report

echo ""
echo "10. Cleanup..."
trap - EXIT
cleanup

echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  DONE! Results in: ${RESULTS_DIR}/FORKS.md"
echo "═══════════════════════════════════════════════════════════"
