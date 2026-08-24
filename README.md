# MkAIHub

MkAIHub 是公司内部的轻量级 AI 分享平台，用于发布、检索和复用提示词、代码、工作流、文档等实践成果，并提供任务、知识库、Issues 和竞赛的基础模块入口。首版采用 Vue 3 + TypeScript + Vite、FastAPI + SQLAlchemy + Alembic、SQLite 和本地文件存储，使用单一 Git 仓库管理源码、迁移、文档和部署配置。

## 当前状态

已完成批次 0 工程基线、批次 1 认证导航、批次 2 展品核心、批次 3 任务与 Issues、批次 4 竞赛与管理页：

- Git 单仓库已初始化，默认分支为 `main`。
- `backend/` 已具备认证、管理员用户接口、展品、附件、评论、探索聚合、任务、Issues、竞赛以及评论隐藏/恢复和管理动作结构化日志。
- `frontend/` 已具备登录态路由守卫、展品全流程页面、任务与 Issues 列表/详情/表单页、竞赛列表与详情页；管理页包含用户、内容（展品归档恢复、任务关闭、Issue 关闭重开）、竞赛三个页签，评论隐藏/恢复在对应详情页操作。
- 探索、任务、展品、知识库、Issues、竞赛六个一级路由可访问；未登录用户会进入登录页，普通员工不能进入管理页。
- 首版采用 `EMPLOYEE` 和 `SYSTEM_ADMIN` 两种角色；不开放注册，只由系统管理员维护账号。
- 已创建 `users`、`user_sessions`、`files`、`artifacts`、`artifact_files`、`comments`、`tasks`、`issues`、`competitions` 九张表。
- `deploy/` 已提供单应用 Dockerfile 与 Docker Compose，Vue 生产构建可由 FastAPI 托管。
- `backend_c#/` 的 .NET 6 移植版已对齐 Python 版批次 1–5 全部接口与 CLI（任务、Issues、竞赛、管理动作、结构化日志、备份恢复），34 项 xUnit 测试通过。
- 两份项目方案已校准知识库边界：知识库与展品类型相互独立，首版占位是主动控制范围。

当前进入第二阶段，开发主线优先完善 Python 版。业务主链路调整为“竞赛包含多个任务，参赛人通过任务提交关联展品成果，再按任务评分汇总竞赛结果”；普通独立任务继续保留参与、提交和验收闭环。C# 版暂缓同步。问题定义、实施边界、研发批次和验收标准见 [`docs/MkAIHub第二阶段闭环实施方案.md`](docs/MkAIHub第二阶段闭环实施方案.md)。

数据库与上传文件的备份、恢复已实现并完成一次实际恢复演练（见 `deploy/BACKUP.md`）。按首版简化决定，容器重启验证与浏览器自动化测试不纳入本仓库交付，部署环境可按文档自行执行；知识库仍是占位页。首版安全保持轻量，只实现密码哈希、服务端会话、HttpOnly Cookie、简单 CSRF、角色校验和附件的大小/扩展名基础限制，不加入来源策略、限流、设备/IP 风控、病毒扫描或复杂管理员治理。

Element Plus 已锁定为表单和后台组件依赖，但不做全局整库注册；后续页面按实际使用的组件引入，避免无业务功能的初始化骨架承担整库首包体积。

## 目录约定

```text
MkAIHub/
  backend/               # FastAPI、SQLAlchemy、Alembic、Pytest
  frontend/              # Vue、Vite、TypeScript、npm
  docs/                  # 产品规划、首版方案与第二阶段闭环方案
  prototype/             # 现有静态原型，仅作迁移参考
  deploy/                # Dockerfile 与 Docker Compose
  data/                  # SQLite 运行数据，不提交
  storage/uploads/       # 上传文件，不提交
```

部署形态是一个 FastAPI 单应用容器：FastAPI 提供 REST API，并托管前端生产构建产物；SQLite 数据库和 `storage/uploads/` 通过 Docker Compose 映射到宿主机。正式环境保持单个 Uvicorn worker，不拆分数据库、前端和后台服务。

## 本地开发

准备 Python 3.13、uv、Node.js（方案基线为 v24）和 npm。先在项目根目录复制环境模板，并设置仅本机使用的随机 `APP_SECRET_KEY`：

```powershell
Copy-Item .env.example .env
```

后端命令目标（在 `backend/` 目录执行）：

```powershell
Set-Location backend
uv sync --locked
uv run alembic upgrade head
uv run python -m app.cli create-admin
uv run uvicorn app.main:app --reload
```

`create-admin` 会交互式询问用户名、显示名称和密码，不会自动生成默认密码。密码首版只校验 8–128 位；管理员创建后即可通过前端登录。

前端命令目标（另开终端，在 `frontend/` 目录执行）：

```powershell
Set-Location frontend
npm ci
npm run dev
```

前端开发服务器会把 `/api` 请求代理到 `http://127.0.0.1:8000`，因此后端按上述默认端口启动即可联调。Linux/macOS 使用等价的 `cp .env.example .env`、`cd backend` 和 `cd frontend` 命令即可。后端无论从项目根目录还是 `backend/` 启动，都固定读取仓库根目录的 `.env`；不需要维护第二份配置文件。生产 Compose 使用命令行 `--env-file .env` 将同一份根目录配置传入容器。

## 迁移、测试与构建入口

后端迁移和测试：

```text
cd backend
uv run alembic upgrade head
uv run pytest
```

前端类型检查、测试和生产构建：

```text
cd frontend
npm run type-check
npm run test
npm run build
```

`uv sync --locked` 依赖提交的 `backend/uv.lock`；`npm ci` 依赖提交的 `frontend/package-lock.json`。

本轮已实际验证：

- `uv sync --locked`、Alembic 从空库升级和 `uv run pytest` 通过（28 项后端测试）。
- `npm ci`、类型检查、`npm run test` 和生产构建通过（35 项前端测试）。
- FastAPI 实际启动后，健康检查、认证、用户管理、展品、附件、评论、探索、任务、Issues、竞赛接口通过；竞赛创建/列表/删除和隐藏评论对员工不可见等管理行为经真实请求冒烟验证，管理动作以结构化 JSON 日志输出（含 action、actor_id、target_type、target_id）。
- 备份与恢复：`python -m app.cli backup` / `restore` 已完成一次完整演练——备份后新增账号与改动上传目录，恢复后数据库与文件均回到备份点（记录见 `deploy/BACKUP.md`）。
- 浏览器实际检查管理员和员工登录、刷新保留会话、创建用户、权限拦截、自助改密和中文界面通过，控制台无错误或警告。
- `docker compose config` 通过；本机 Docker 服务未运行，因此尚未构建和启动镜像。
- C# 版批次 6 对齐：`dotnet test` 34 项通过；`dotnet run --project MkAIHub.Api -- backup/restore` 用演示库完成一次真实备份与恢复（含上传目录）。
- 两后端互通冒烟：C# 版直接读写 `data/batch1-integration.db`——可校验 Python 版创建的 Argon2 密码登录、读出全部业务数据，C# 写入的任务可被 Python ORM 回读一致；演示库已还原到冒烟前状态。

## 当前工作区演示账号

本地浏览器联调使用的演示数据库保留在 `data/batch1-integration.db`。该文件受 `.gitignore` 排除，只用于当前工作区演示，不是生产数据，也不会随 Git 源码分发。

| 角色 | 用户名 | 密码 |
|---|---|---|
| 系统管理员 | `admin` | `Admin1234` |
| 普通员工 | `employee` | `Employee456` |

从 `backend/` 启动该演示库：

```powershell
$env:DATABASE_URL = "sqlite:///../data/batch1-integration.db"
uv run uvicorn app.main:app --reload
```

演示密码仅为本地体验准备，禁止复制到正式环境。正式部署必须通过 `create-admin` 单独创建管理员并使用独立密码。

## Docker Compose 部署

Linux 单机部署使用 `deploy/docker-compose.yml`，只启动一个 `app` 服务。Dockerfile 的构建阶段执行 `npm ci`、`npm run build` 和 `uv sync --locked`，运行阶段由 FastAPI 托管 `frontend/dist`，并以一个 Uvicorn worker 启动。

```bash
cp .env.example .env
# 编辑 .env：设置随机 APP_SECRET_KEY、APP_ENV=production，
# 并将 FRONTEND_ORIGIN 改为用户实际访问地址。
docker compose --env-file .env -f deploy/docker-compose.yml config
docker compose --env-file .env -f deploy/docker-compose.yml up -d --build
docker compose --env-file .env -f deploy/docker-compose.yml exec app alembic upgrade head
docker compose --env-file .env -f deploy/docker-compose.yml exec app python -m app.cli create-admin
```

`data/` 映射到容器的 `/app/data`，`storage/uploads/` 映射到 `/app/storage/uploads`；两者位于宿主机并随容器重启保留。首次使用时 Docker Compose 会创建这两个目录，Linux 主机应确保运行容器的用户具有读写权限。健康检查目标为 `/api/health`。数据库与上传文件的备份、恢复命令和演练记录见 `deploy/BACKUP.md`。

## 环境变量

`.env.example` 覆盖首版方案要求的应用名称、密钥、SQLite 地址、数据/上传目录、上传限制、会话、前端来源、日志级别和建议的初始管理员用户名。应用不会根据该用户名自动创建账号；管理员必须显式执行 `create-admin`。初始密码、生产密钥、`.env`、SQLite 数据库和上传文件都不得提交 Git。生产 Compose 会将数据库和目录路径切换为容器内的持久化挂载路径。

## 首版边界

展品是首版核心，目标是完成创建、编辑、发布、检索、附件和评论闭环；任务与 Issues 提供基础 CRUD/状态骨架，竞赛提供展示和管理员维护，知识库只保留占位页，探索页只聚合模块入口和最近发布的展品。首版不包含 Toolkits、Data Lab、MapOS、Agent 自动执行、复杂协作、支付、SSO/MFA、独立知识库数据模型或多节点部署。

批次 0 至批次 4 已完成代码实现，包含工程基线、认证、用户管理、展品核心闭环、探索页最新展品、任务与 Issues 基础流程、竞赛展示与维护、单页管理（用户、内容、竞赛）和各列表的“只看我的”筛选。批次 5 按简化范围完成：部署配置（单容器 Dockerfile 与 Compose、卷持久化）、备份恢复命令与说明、初始化说明和回归测试；容器重启验证与浏览器自动化测试不纳入交付。批次 6 完成 C# 移植版与 Python 版的功能对齐（任务、Issues、竞赛、管理动作、结构化日志、备份恢复），并以共用 SQLite 数据库的方式实测两后端双向互通。
