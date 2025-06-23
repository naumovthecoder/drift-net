DEV_BACKEND_PORT ?= 5080
DEV_FRONTEND_PORT ?= 5173

.PHONY: dev clean reset

dev:
	@echo "Starting backend and frontend..."
	# Run backend with dotnet watch
	(cd backend && dotnet watch run --urls http://localhost:$(DEV_BACKEND_PORT)) & \
	# Run frontend via vite
	(cd frontend && npm run dev -- --port $(DEV_FRONTEND_PORT)) 

# Stop dev processes and remove containers/images for a fresh start
clean:
	@echo "Stopping dev processes & cleaning Docker..."
	# Try multiple patterns to be sure
	-pkill -f "dotnet watch" || true
	-pkill -f "dotnet.*ControlApi" || true
	-pkill -f "ControlApi.dll" || true
	-pkill -f vite || true
	-kill $(lsof -ti:5080)
	# Kill whatever still holds 5080
	lsof -ti:5080 | xargs -r kill -9
	sleep 1
	# Bring down previously generated compose stack if present
	@if [ -f backend/docker-compose.generated.yml ]; then \
		docker compose -f backend/docker-compose.generated.yml down -v --remove-orphans || true; \
	fi
	# Remove any running driftnode containers left behind
	-docker ps -aq --filter "label=com.docker.compose.service=driftnode" | xargs -r docker rm -f
	# Optional: prune dangling images
	-docker image prune -f
	# Take down root compose if exists
	docker compose -f ../docker-compose.yml down -v --remove-orphans || true

# Full reset then start dev stack
reset: clean dev 