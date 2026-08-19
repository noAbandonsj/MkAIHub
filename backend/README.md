# MkAIHub backend

The backend is a Python 3.13 FastAPI application managed with `uv`.

From this directory, the normal local commands are:

```text
uv sync --locked
uv run alembic upgrade head
uv run python -m app.cli create-admin
uv run pytest
uv run uvicorn app.main:app --reload
```

`create-admin` interactively creates the first `SYSTEM_ADMIN`; the application
does not create a default account or password automatically. Passwords are
limited to 8-128 characters in the lightweight first version.

The default health endpoint is `GET /api/health`. Authentication endpoints are
under `/api/v1/auth`, while administrator user-management endpoints are under
`/api/v1/admin/users`. Runtime data is kept outside the source package: the
configured local SQLite database is under `data/`, and uploads are stored under
`storage/uploads`.

Migration head `20260819_0002` creates the first-version `users` and
`user_sessions` tables. Business tables are intentionally deferred to later
development batches.
