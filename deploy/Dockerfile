# syntax=docker/dockerfile:1

# Build the Vue application first. The frontend directory and package lock are
# created in the frontend implementation batch.
FROM node:24-bookworm-slim AS frontend-builder

WORKDIR /build/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# Resolve the locked Python environment with uv. Keeping this in a separate
# stage prevents uv and build-only files from being required at runtime.
FROM ghcr.io/astral-sh/uv:python3.13-bookworm-slim AS backend-builder

WORKDIR /build/backend
ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy
COPY backend/pyproject.toml backend/uv.lock ./
RUN uv sync --locked --no-dev --no-install-project
COPY backend/ ./
RUN uv sync --locked --no-dev

# The final image contains only the Python runtime, the locked virtualenv,
# FastAPI application, and the built frontend assets.
FROM python:3.13-slim-bookworm AS runtime

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/app/backend/.venv/bin:${PATH}" \
    APP_ENV=production \
    DATA_DIR=/app/data \
    UPLOAD_DIR=/app/storage/uploads \
    DATABASE_URL=sqlite:////app/data/mkaihub.sqlite3 \
    FRONTEND_DIST_DIR=/app/frontend/dist

WORKDIR /app
COPY --from=backend-builder /build/backend /app/backend
COPY --from=frontend-builder /build/frontend/dist /app/frontend/dist
RUN mkdir -p /app/data /app/storage/uploads

WORKDIR /app/backend
EXPOSE 8000

# The first release deliberately runs one Uvicorn worker because SQLite is a
# local single-instance database in the deployment plan.
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "1"]
