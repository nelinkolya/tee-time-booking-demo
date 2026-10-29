#!/usr/bin/env bash
# Throwaway SQL Server for one CI job. The sa password is generated per run and masked, never stored.
set -euo pipefail

password="Ci-$(openssl rand -hex 16)-Aa1"
echo "::add-mask::${password}"
echo "SQL_SA_PASSWORD=${password}" >> "${GITHUB_ENV}"

docker run --detach --name sqlserver \
  --env ACCEPT_EULA=Y \
  --env MSSQL_SA_PASSWORD="${password}" \
  --publish 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest

for attempt in $(seq 1 60); do
  if docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "${password}" -Q "SELECT 1" > /dev/null 2>&1; then
    echo "SQL Server is ready (attempt ${attempt})."
    exit 0
  fi
  sleep 2
done

echo "SQL Server did not become ready in time." >&2
docker logs sqlserver >&2
exit 1
