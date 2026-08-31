#!/usr/bin/env bash
# Dry-run al migrărilor onboarding pe un Postgres throwaway (podman).
#
# Fără argumente  -> smoke test pe o bază GOALĂ (verifică doar că toate migrările se aplică).
# Cu un dump      -> ./migration-dryrun.sh /cale/catre/dump.sql   (validează grandfathering-ul
#                    pe date reale: userii Approved rămân înrolați, cei în curs pe pasul corect).
#
# Rulează de oriunde; folosește portul 5434 ca să NU atingă baza de dev de pe 5433.
set -euo pipefail

DUMP="${1:-}"
PORT="${DRYRUN_PORT:-5434}"
CONTAINER="ridelance-dryrun"
DB="ridelance"
PGUSER="postgres"
PGPASS="postgres"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND="$(cd "$HERE/.." && pwd)"
SQL_OUT="$(mktemp /tmp/ridelance-migrations.XXXX.sql)"

export PATH="$PATH:$HOME/.dotnet/tools"

cleanup() { podman rm -f "$CONTAINER" >/dev/null 2>&1 || true; rm -f "$SQL_OUT"; }
trap cleanup EXIT

echo "==> Pornesc Postgres throwaway pe portul $PORT..."
podman rm -f "$CONTAINER" >/dev/null 2>&1 || true
podman run -d --name "$CONTAINER" \
  -e POSTGRES_USER="$PGUSER" -e POSTGRES_PASSWORD="$PGPASS" -e POSTGRES_DB="$DB" \
  -p "$PORT:5432" docker.io/library/postgres:16 >/dev/null

echo "==> Aștept baza să fie gata..."
for _ in $(seq 1 30); do
  if podman exec "$CONTAINER" pg_isready -U "$PGUSER" >/dev/null 2>&1; then break; fi
  sleep 1
done

if [[ -n "$DUMP" ]]; then
  echo "==> Restaurez dump-ul de producție ($DUMP)..."
  PGPASSWORD="$PGPASS" podman exec -i "$CONTAINER" psql -U "$PGUSER" -d "$DB" < "$DUMP"
fi

echo "==> Generez scriptul idempotent de migrare..."
dotnet-ef migrations script --idempotent \
  --project "$BACKEND/src/Infrastructure" \
  --startup-project "$BACKEND/src/Web.Api" \
  --context ApplicationDbContext -o "$SQL_OUT" >/dev/null

echo "==> Aplic migrările..."
PGPASSWORD="$PGPASS" podman exec -i "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$PGUSER" -d "$DB" < "$SQL_OUT"
echo "    Toate migrările s-au aplicat fără erori."

echo "==> Verificări post-migrare:"
PGPASSWORD="$PGPASS" podman exec -i "$CONTAINER" psql -U "$PGUSER" -d "$DB" <<'SQL'
\pset footer off
-- Tabelele noi există
SELECT 'tabele noi' AS check,
       to_regclass('public.pfa_vehicles') IS NOT NULL
       AND to_regclass('public.vehicle_copy_requests') IS NOT NULL
       AND to_regclass('public.vehicle_badges') IS NOT NULL
       AND to_regclass('public.extracted_fields') IS NOT NULL
       AND to_regclass('public.app_settings') IS NOT NULL AS ok;

-- Coloanele de parolă au dispărut
SELECT 'parole șterse' AS check,
       NOT EXISTS (
         SELECT 1 FROM information_schema.columns
         WHERE table_name = 'pfa_platform_accounts'
           AND column_name IN ('password_protected','password_updated_at_utc')
       ) AS ok;

-- Grandfathering: orice dosar Approved are onboarding_completed_at_utc setat
SELECT 'approved înrolați' AS check,
       NOT EXISTS (
         SELECT 1 FROM pfa_registrations
         WHERE status = 'Approved' AND onboarding_completed_at_utc IS NULL
       ) AS ok;
SQL

echo "==> Dry-run încheiat cu succes."
