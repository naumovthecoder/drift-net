# Drift Control-Deck

Web-panel to launch, scale and monitor a local **DriftNet** network. Consists of:

* **backend/** – ASP.NET 8 Minimal API (`ControlApi`)
* **frontend/** – React + Vite + Tailwind UI

## Requirements

* Docker / Docker Compose v2
* .NET SDK 8.0
* Node >=18 & pnpm / npm

## Quick start

```bash
# install JS deps
cd drift-control-deck/frontend && npm install
# run everything (backend + frontend with hot-reload)
make -C drift-control-deck dev
```

Open http://localhost:5173 and enjoy.

## Environment variables

Create `.env` inside `backend/` or in repo root:

```
API_TOKEN=secret-token            # dev auth header (x-api-token)
ComposeTemplate=compose.template.yml  # optional override
ComposeOutput=docker-compose.generated.yml
ProfilesDbPath=profiles.db
```

## REST API

| Method | Path | Description |
| ------ | ---- | ----------- |
| POST   | /api/launch | Launch DriftNet with N nodes |
| PATCH  | /api/scale  | Change replicas of driftnode |
| GET    | /api/metrics | Stats per node |
| POST   | /api/cmd | Send RECOVERY / NORMAL to nodes |
| POST   | /api/upload | Upload data stream |
| GET    | /api/download/:streamId | Download stream |
| WS     | /ws/metrics | Real-time metrics push |
| CRUD   | /api/profiles | Manage ENV profiles |

## Profiles example

`profiles/dev.json`:

```json
{
  "name": "Low latency",
  "env": {
    "DELAY_MS": "10",
    "PACKET_LOSS": "0.01"
  }
}
```

## Build & ship

```bash
cd drift-control-deck/backend
docker build -t control-api:latest .
``` 