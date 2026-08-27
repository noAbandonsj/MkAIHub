# MkAIHub backend (C# / .NET 6)

本目录是 `backend/`（Python FastAPI）后端的 C# 迁移版本，功能与 Python 版保持一致，
使用 .NET 6 与 ASP.NET Core (Minimal Hosting + Controllers)、EF Core 6 (SQLite)、
Konscious Argon2。**只新增/修改本目录**，与现有 `backend/`、`frontend/`、`deploy/` 互不影响。

## 与 Python 版的对应关系

| Python (backend/)                    | C# (backend_c#)                            |
| ------------------------------------ | ------------------------------------------ |
| `app.main:create_app`                | `MkAIHub.Api/Program.cs`                   |
| `app.core.config.Settings` (.env)    | `MkAIHub.Api/Core/Settings.cs`             |
| `app.core.errors` (AppError)         | `MkAIHub.Api/Core/AppError.cs`             |
| `app.core.logging` (JSON 日志)        | `MkAIHub.Api/Core/JsonLogging.cs`          |
| `app.models.*` (SQLAlchemy)          | `MkAIHub.Api/Data/Entities.cs`             |
| `app.db.session` (engine/pragma)     | `MkAIHub.Api/Data/AppDbContext.cs`         |
| `alembic upgrade head`               | `MkAIHub.Api/Data/Migrator.cs`             |
| `app.services.security` (argon2-cffi)| `MkAIHub.Api/Security/PasswordHasher.cs`   |
| `app.services.sessions/storage` 等   | `MkAIHub.Api/Services/*`                   |
| `app.api.health / v1.*` 路由          | `MkAIHub.Api/Api/Controllers.*.cs`         |
| `app.api.v1.deps`（会话/CSRF）        | `MkAIHub.Api/Api/Authenticator.cs`         |
| `app.cli create-admin`               | `MkAIHub.Api/Cli/CliCommands.cs`           |
| `tests/` (pytest)                    | `MkAIHub.Api.Tests`（xUnit，用例一一移植） |

## 兼容性要点

- **API 契约一致**：路径（`/api/health`、`/api/v1/...`）、snake_case 字段、UTC ISO 时间
  （`...Z` 后缀）、统一错误体 `{code, message, details?}`、CSRF 头 `X-CSRF-Token`、
  会话 Cookie（HttpOnly / SameSite=Lax / Secure 仅生产）全部与 Python 版一致。
- **数据库可直接互换**：迁移器复刻 Alembic 各 revision 的 DDL 并维护 `alembic_version`
  表（head 为 `20260819_0007`，含批次 8/9 的任务与竞赛闭环五张新表），时间戳采用与
  SQLAlchemy 相同的文本格式；`Numeric` 得分列与 SQLAlchemy 一致以 REAL/INTEGER 存储，
  C# 读取时经最短往返字符串还原为精确 `decimal`；两个后端可共用同一个 SQLite 文件
  （已双向验证）。
- **密码哈希互通**：Argon2id（m=19456, t=2, p=2, salt 16B, hash 32B），
  Python argon2-cffi 生成的哈希可被 C# 校验，反之亦然（已双向验证）。
- **默认端口 8000**（与 uvicorn 一致）；上传保存路径 `yyyy/MM/<uuid>.<ext>`、
  大小上限、扩展名白名单、SHA256 摘要逻辑相同。

## 本地运行

在本目录（`backend_c#`）下执行，环境变量与 `.env` 约定和 Python 版相同
（仓库根目录的 `.env` 会被读取，且真实环境变量优先）：

```text
dotnet build
dotnet run --project MkAIHub.Api -- migrate          # 相当于 alembic upgrade head
dotnet run --project MkAIHub.Api -- create-admin     # 交互式创建首个 SYSTEM_ADMIN
dotnet test                                          # 运行移植的测试套件
dotnet run --project MkAIHub.Api                     # 启动服务，默认 http://0.0.0.0:8000
```

提示：

- `create-admin` 支持 `--username/--display-name/--password/--database-url` 参数；
  未提供密码时会以掩码方式交互确认（密码 8-128 位）。
- 服务不会自动建表或自动创建账号，请先执行 `migrate` 与 `create-admin`，
  与 Python 版流程一致。
- `frontend/dist` 存在时自动托管 SPA（未知非 API 路径回退到 `index.html`）；
  否则 `GET /` 返回服务信息 JSON。

## 环境变量

与 Python 版完全同名同义（`APP_ENV`、`APP_SECRET_KEY`、`DATABASE_URL`、
`UPLOAD_DIR`、`MAX_UPLOAD_SIZE_MB`、`SESSION_TTL_HOURS`、`FRONTEND_ORIGIN`、
`LOG_LEVEL` 等）。`DATABASE_URL` 接受 `sqlite:///相对或绝对路径`（例如
`sqlite:///../data/mkaihub.sqlite3`，相对当前工作目录解析）或普通文件路径。

## 测试

`MkAIHub.Api.Tests` 将 Python `tests/` 下的 health、auth、artifacts、config、
database、cli、任务/竞赛闭环与工作台用例逐条移植为 xUnit（47 个），并额外包含
Python 生成的 Argon2 哈希常量用于跨后端校验。每个用例使用独立的临时 SQLite 库与
上传目录。

## 当前对齐状态

已对齐 Python 版批次 1–15 全部后端功能（认证、展品、任务与 Issues、竞赛、管理动作、
结构化日志、备份恢复命令，以及第二阶段的任务参与/提交/验收闭环、竞赛报名/评审/
计分/结果发布、个人工作台与跨模块筛选、已发布竞赛编辑锁定修复、存在草稿竞赛时
员工看不到独立任务的修复）。批次 7/12 的文档与批次 13 的前端改动不属于本目录范围；
自批次 14 重新对齐起，本目录与 Python 版保持同步修改。
