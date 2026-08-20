# MkAIHub 工作区说明

公司内部轻量级 AI 分享平台（发布/检索/复用提示词、代码、工作流等）。单仓库：
Vue 3 + TypeScript + Vite 前端、FastAPI + SQLAlchemy + Alembic 后端、SQLite、本地文件存储。
文档、提交信息和界面均为中文；提交信息使用「批次N范围：内容」格式（如 `批次2后端：展品核心模型、接口与测试`）。

## 目录

- `backend/` — Python 3.13 FastAPI（uv 管理）。分层：`app/api/v1/*` 路由 → `app/services/*` 业务 → `app/models/*` SQLAlchemy 模型；`app/schemas/*` Pydantic、`app/core/*` 配置/错误/日志/安全、`app/db/*` 会话、`migrations/` Alembic。
- `backend_c#/` — Python 后端的 C#/.NET 6 移植版（EF Core 6 + xUnit），**尚未提交 Git**。与 Python 版 API 契约、SQLite 数据库、Argon2 密码哈希完全互通，可共用同一个数据库文件。改动 C# 版时只动本目录，不碰 `backend/`。
- `frontend/` — Vue 3 + Pinia + vue-router + Element Plus + markdown-it。`src/api/` 接口封装、`src/components/<域>/`、`src/views/<域>/`、`src/stores/`、`src/router/`；测试与源码同目录（`*.test.ts`）。
- `docs/` — `MkAIHub项目规划.md` 与 `MkAIHub首版实现方案.md`，改动范围/边界前必读。
- `prototype/` — 静态 HTML 原型，仅作迁移参考，不维护。
- `deploy/` — 单应用容器的 Dockerfile 与 docker-compose.yml。
- `data/`、`storage/` — 运行数据与上传文件，已 gitignore，不得提交。

## 常用命令

后端（在 `backend/` 下执行）：

```text
uv sync --locked
uv run alembic upgrade head        # 迁移头 20260819_0004
uv run python -m app.cli create-admin
uv run pytest                       # 后端测试
uv run uvicorn app.main:app --reload
```

前端（在 `frontend/` 下执行）：

```text
npm ci
npm run dev        # 端口 5173，/api 代理到 http://127.0.0.1:8000
npm run type-check # vue-tsc，无 ESLint/Prettier，以此为准
npm run test       # vitest
npm run build      # 先跑 type-check 再 vite build
```

C# 后端（在 `backend_c#/` 下执行）：`dotnet build`、`dotnet test`、
`dotnet run --project MkAIHub.Api -- migrate`（相当于 alembic upgrade head）、
`dotnet run --project MkAIHub.Api -- create-admin`、`dotnet run --project MkAIHub.Api`。

## 约定与边界

- API 契约：路径 `/api/health`、`/api/v1/{auth,admin/users,artifacts,tasks,issues,files,explore}`；字段 snake_case；UTC ISO 时间带 `Z` 后缀；错误体统一 `{code, message, details?}`。两个后端必须保持一致。
- 认证：服务端会话 + HttpOnly Cookie（`mkaihub_session`）+ CSRF 头 `X-CSRF-Token`；角色仅 `EMPLOYEE` / `SYSTEM_ADMIN`，不开放注册；密码 Argon2id（m=19456, t=2, p=2），两后端哈希互通。
- Alembic 迁移文件名用日期前缀（`20260819_000N_*`）；C# 版 `MkAIHub.Api/Data/Migrator.cs` 复刻同一套 DDL 并维护 `alembic_version`，新增迁移时两处需同步。
- 前端 Element Plus 按组件逐个引入，禁止全局整库注册；`@` 别名指向 `frontend/src`；代码风格为无分号、单引号。
- `.env` 固定从仓库根目录读取（无论从哪个目录启动）；环境变量见 `.env.example`。`.env`、数据库、上传文件、初始密码不得提交。
- 首版边界（见 docs/）：竞赛仅有占位路由，知识库为占位页；安全保持轻量，不加限流/病毒扫描/SSO 等。
- 本地演示库 `data/batch1-integration.db`（admin/Admin1234、employee/Employee456）仅限本机联调，禁止用于正式环境。
- 部署形态为单容器单 Uvicorn worker：FastAPI 同时托管 `frontend/dist`；SQLite 与 `storage/uploads/` 通过 Compose 卷挂载。
