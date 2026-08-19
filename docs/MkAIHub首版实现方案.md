# MkAIHub 首版实现方案

> 文档状态：首版实施基线  
> 版本：v0.6  
> 更新日期：2026-08-19  
> 前置文档：[MkAIHub 项目规划](./MkAIHub项目规划.md)

## 1. 文档目的

本文档把 MkAIHub 的总体规划拆成可执行的首版产品和研发方案，用于后续初始化工程、设计数据库、实现接口、迁移原型和组织验收。

首版建设重点是：

1. 搭建“探索、任务、展品、知识库、Issues、竞赛”六个一级模块的真实页面骨架。
2. 将“展品”作为首版核心，完成创建、编辑、发布、检索、附件和评论闭环。
3. 让探索页使用最少聚合查询，知识库只保留导航和占位页面。
4. 让任务、Issues 和竞赛达到可使用的基础版本，但不提前实现复杂协作和运营流程。

在满足上述目标的前提下，首版默认选择字段更少、状态更少、表更少和页面更少的实现；仅在业务确认需要时增加可选能力。

系统显示名称统一为 **MkAIHub**。

## 2. 首版范围结论

### 2.1 模块范围矩阵

| 模块 | 首版实现深度 | 首版主要能力 | 明确后置能力 |
|---|---|---|---|
| 探索 | 最小入口页 | 六个模块入口、最近发布的 6 条展品 | 多模块聚合、推荐算法、排行 |
| 任务 | 基础骨架 | 列表、详情、创建、编辑、状态管理 | 报名、领取、参与、成果提交、验收 |
| 展品 | 首版核心 | 无类型的完整 CRUD、发布、附件、检索、评论；全员可见 | 类型、标签、点赞、封面、版本管理、审批流 |
| 知识库 | 占位骨架 | 独立导航和占位页面 | 数据模型、列表、详情、文档解析、RAG、问答 |
| Issues | 基础闭环 | 列表、详情、发起、编辑、评论、关闭/重开 | 指派、优先级工作流、里程碑、看板 |
| 竞赛 | 展示与维护骨架 | 列表、详情、管理员创建和维护 | 报名、组队、提交、评审、排行榜 |

### 2.2 首版不包含

- Toolkits、Data Lab、MapOS。
- AI Agent 自动接取或执行任务。
- 任务参与人、成果、验收和成果转展品。
- 竞赛报名、队伍、作品、评分和排名。
- 独立知识库数据结构、全文解析、向量库和 RAG。
- 自主注册、验证码、找回密码、首次强制改密、SSO 和 MFA。
- 复杂密码规则、登录限流、来源策略、设备/IP 风控、会话管理页面和复杂管理员治理。
- 展品类型、分类、标签、点赞、独立封面、复杂组织树和内容审批流。
- 独立工作台、审计日志查询页面和拆分式管理后台。
- 消息中心、实时聊天、邮件或企业 IM 通知。
- 支付、奖金、积分和财务结算。

## 3. 用户与权限

### 3.1 角色

#### 普通员工 `EMPLOYEE`

- 浏览六个业务模块。
- 创建、编辑、发布和归档自己的展品。
- 创建和维护自己的任务。
- 发起和维护自己的 Issue。
- 对已发布展品和开放 Issue 评论。
- 查看竞赛信息。

#### 系统管理员 `SYSTEM_ADMIN`

- 具备普通员工权限。
- 创建、编辑、停用用户并重置密码。
- 分配角色。
- 归档或恢复异常展品。
- 关闭不合适的任务，关闭或重开 Issue。
- 创建和维护竞赛。
- 隐藏或恢复不合适的评论。
- 查看系统状态和服务器日志。

首版只有 `EMPLOYEE` 和 `SYSTEM_ADMIN` 两种角色，不设置内容管理员等中间管理角色。

### 3.2 权限原则

- 未登录用户只能访问登录页和健康检查接口。
- 作者只能修改自己拥有的资源，管理员的内容管理操作不能冒充作者改写正文。
- 普通员工不能调用管理接口，即使前端手工构造请求也必须返回 `403`。
- 状态切换使用独立动作接口，不允许通过普通 `PATCH` 绕过状态机。
- 所有已发布展品对全部登录员工可见，不设置人员、部门或私有可见范围。
- 已归档展品不出现在普通列表和探索页中，但作者与管理员可通过“只看我的”或管理页查看。
- 关键管理动作写入后端结构化服务器日志，首版不建立审计数据表。

## 4. 导航与页面路由

### 4.1 一级导航

```text
探索 / 任务 / 展品 / 知识库 / Issues / 竞赛
```

登录后默认进入 `/explore`。

### 4.2 业务页面

| 路由 | 页面 | 访问范围 |
|---|---|---|
| `/login` | 登录页 | 未登录 |
| `/explore` | 探索入口页 | 已登录 |
| `/tasks` | 任务列表 | 已登录 |
| `/tasks/new` | 新建任务 | 已登录 |
| `/tasks/:id` | 任务详情 | 已登录 |
| `/tasks/:id/edit` | 编辑任务 | 作者/管理员 |
| `/artifacts` | 展品列表 | 已登录 |
| `/artifacts/new` | 新建展品 | 已登录 |
| `/artifacts/:id` | 展品详情 | 按可见性 |
| `/artifacts/:id/edit` | 编辑展品 | 作者 |
| `/knowledge` | 知识库列表 | 已登录 |
| `/issues` | Issues 列表 | 已登录 |
| `/issues/new` | 发起 Issue | 已登录 |
| `/issues/:id` | Issue 详情 | 已登录 |
| `/competitions` | 竞赛列表 | 已登录 |
| `/competitions/:id` | 竞赛详情 | 已登录 |

知识库首版没有详情路由和后端接口。

### 4.3 管理页面

首版只提供一个系统管理员路由 `/admin`，页面使用“用户、内容、竞赛”三个页签，不拆分多个后台路由。普通员工看不到入口且不能调用管理接口。

## 5. 页面实现说明

### 5.1 探索页

探索页不保存独立数据，只显示六个一级模块的固定入口和最近发布的 6 条展品。展品按 `published_at` 倒序，不计算热门，不聚合任务、Issues 或竞赛。没有展品时显示空状态，不放置虚假演示内容。

### 5.2 任务

任务字段：

- 标题。
- 任务描述。
- 发布人。
- 截止时间，可空。
- 状态。
- 创建和更新时间。

状态：

```text
OPEN（开放） -> COMPLETED（完成）
           \-> CLOSED（关闭）
```

规则：

- 任务创建后直接进入 `OPEN`，不提供任务草稿和单独发布动作。
- 作者可以在开放期间编辑、标记完成或关闭任务。
- 开放任务对所有登录员工可见。
- 首版的“完成”是发布人手工确认，不关联成果或验收记录。
- 已完成和已关闭任务只读，不提供重开和物理删除。

### 5.3 展品

展品是统一的 AI 分享载体，首版不区分内容类型。

展品字段：

- 标题。
- 摘要。
- Markdown 正文。
- 最多 10 个附件。
- 作者、状态和时间信息。

状态：

```text
DRAFT（草稿） -> PUBLISHED（已发布） -> ARCHIVED（已归档）
```

规则：

- 作者可直接发布，不经过审批。
- 草稿只有作者和管理员可见。
- 已发布展品对所有登录员工可见，并进入探索页和展品列表。
- 作者可以把自己的已发布展品归档；系统管理员可以因内容问题归档。
- 归档后可以恢复为已发布状态。
- 草稿可由作者删除；发布后的展品不做物理删除。
- 首版不记录类型、点赞、浏览量，也不提供独立封面；列表统一使用默认色块。

评论无需发布前审核。登录员工可以直接发布评论；评论作者可以删除自己的评论，系统管理员可以隐藏或恢复评论。

### 5.4 知识库

知识库首版只实现 `/knowledge` 导航和占位页面：

- 原型中的知识库与展品是两个独立模块，知识库能力不依赖展品类型。
- 显示简短说明和“返回展品”入口。
- 不展示列表，不提供创建和详情功能。
- 不建立 `knowledge` 表，也不提供知识库 API。
- 首版占位是主动控制实施范围，并非删除展品类型后的技术限制。
- 后续需求明确后，可建设独立知识模型，或通过显式关系关联展品；不默认按展品类型复用数据。

### 5.5 Issues

Issue 用于记录平台建议、问题和内部讨论。首版不设置 Issue 类型、负责人、优先级或标签。

状态：

```text
OPEN（开放） <-> CLOSED（关闭）
```

规则：

- 员工可以发起 Issue。
- 发起人可在 Issue 开放期间编辑标题和正文。
- 发起人和管理员可以关闭或重开 Issue。
- 登录员工可对开放 Issue 评论。
- 关闭后正文和评论只读。
- 首版不实现负责人、优先级、类型、标签、迭代或看板。

### 5.6 竞赛

竞赛字段：

- 标题。
- 简介。
- Markdown 规则。
- 开始和结束时间。
- 创建人和更新时间。

状态不入库，根据服务器当前时间计算：

```text
UPCOMING（即将开始） -> ONGOING（进行中） -> ENDED（已结束）
```

规则：

- 普通员工只读。
- 系统管理员可以创建和编辑。
- 开始时间必须早于结束时间。
- 无需管理员切换状态，也不增加定时任务。
- 竞赛没有报名、参赛者、作品、评审和排行榜数据表。

### 5.7 个人内容筛选

不建设独立工作台。展品、任务和 Issues 列表各提供一个“只看我的”开关，通过查询参数 `mine=true` 按当前用户筛选。

## 6. 前端实现方案

### 6.1 技术栈

- Vue 3 + TypeScript。
- Vite。
- Vue Router。
- Pinia，仅保存当前用户、角色和少量全局状态。
- Element Plus。
- 浏览器原生 `fetch` 封装 API 客户端。
- CSS Variables + Scoped CSS。
- npm 和 `package-lock.json`。
- 使用系统 Node.js；当前开发环境基线为 Node.js v24.15.0、npm 11.12.1。

### 6.2 推荐目录

```text
frontend/
  src/
    api/
      auth.ts
      explore.ts
      artifacts.ts
      tasks.ts
      issues.ts
      competitions.ts
      admin.ts
    assets/
    components/
      common/
      artifact/
      task/
      issue/
      competition/
    layouts/
      AppLayout.vue
    router/
    stores/
    styles/
      variables.css
      global.css
    types/
    views/
      auth/
      explore/
      artifacts/
      knowledge/
      tasks/
      issues/
      competitions/
      admin/
```

### 6.3 公共组件

优先抽取以下公共组件：

- `PageHeader`：标题、说明和主操作。
- `SearchToolbar`：关键词、状态和“只看我的”筛选。
- `StatusBadge`：统一状态颜色和文案。
- `EmptyState`：空状态和创建入口。
- `MarkdownEditor` / `MarkdownViewer`：简单 Markdown 文本框、预览和安全展示，不集成富文本编辑器。
- `FileUploader` / `AttachmentList`：上传和下载。
- `ArtifactCard`：探索和展品列表复用。
- `CommentList` / `CommentForm`：展品和 Issue 复用。
- `ConfirmAction`：归档、关闭和删除确认。

不要为了复用而抽象任务、Issue、竞赛的通用业务基类；它们的状态和权限不同。

### 6.4 状态管理

Pinia 首版仅维护：

- 当前用户。
- 当前角色和权限判断。
- 会话初始化状态。

列表数据和表单数据保留在页面或组合式函数中，不建立大型全局 Store。服务端是业务状态的唯一事实来源。

### 6.5 原型迁移

- 保留现有导航结构、色彩和卡片视觉方向。
- 首版页头只显示文字名称 `MkAIHub`，不制作 Logo 图片和宣传语。
- 原型的静态数据不进入正式代码。
- 原型脚本的字符串模板改为 Vue 组件。
- Element Plus 负责表单、表格、分页、弹窗、上传等通用交互。
- 探索页和内容卡片保留适量自定义样式，避免整个门户呈现成后台表格系统。

## 7. 后端实现方案

### 7.1 技术栈

- Python 3.13。
- FastAPI。
- Pydantic。
- SQLAlchemy 2.x。
- Alembic。
- SQLite。
- uv 和 `uv.lock`。
- Pytest。

`pyproject.toml` 设置：

```toml
requires-python = ">=3.13,<3.14"
```

### 7.2 模块划分

```text
auth          登录、会话、CSRF、当前用户
users         用户和角色
artifacts     展品、附件和评论
explore       首页聚合查询
tasks         任务 CRUD 和状态
issues        Issues 和评论
competitions  竞赛展示和维护
files         上传、下载和元数据
admin         单页式用户、内容和竞赛管理
```

### 7.3 分层

```text
API Router -> Service -> SQLAlchemy Model -> SQLite
```

- Router 负责请求解析、依赖注入和响应。
- Pydantic Schema 负责输入输出字段与校验。
- Service 负责权限、状态切换和事务。
- Model 负责映射、索引和数据库约束。
- 首版不增加 Repository、领域事件、CQRS 或消息队列。

### 7.4 推荐目录

```text
backend/
  pyproject.toml
  uv.lock
  alembic.ini
  app/
    main.py
    api/
      v1/
    core/
      config.py
      security.py
      errors.py
      logging.py
    models/
    schemas/
    services/
    db/
    tests/
  migrations/
```

## 8. 数据库设计

### 8.1 通用规则

- 主键使用 SQLite `INTEGER PRIMARY KEY`。
- 时间以 UTC 保存，API 返回 ISO 8601，前端按本地时区展示。
- 枚举使用稳定的大写英文字符串。
- 开启外键约束。
- 数据库结构变更必须通过 Alembic。
- 普通业务资源保留创建人和创建、更新时间。
- 状态变更由服务层执行，数据库枚举约束作为第二道保护。

### 8.2 `users`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `username` | VARCHAR(64) | 唯一、必填 |
| `display_name` | VARCHAR(100) | 必填 |
| `password_hash` | VARCHAR(255) | 必填 |
| `role` | VARCHAR(32) | `EMPLOYEE/SYSTEM_ADMIN` |
| `is_active` | BOOLEAN | 默认 true |
| `last_login_at` | DATETIME | 可空 |
| `created_at` | DATETIME | 必填 |
| `updated_at` | DATETIME | 必填 |

### 8.3 `user_sessions`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `user_id` | INTEGER | 外键 `users.id` |
| `token_hash` | VARCHAR(128) | 唯一，只保存令牌哈希 |
| `expires_at` | DATETIME | 必填 |
| `last_seen_at` | DATETIME | 必填 |
| `created_at` | DATETIME | 必填 |
| `revoked_at` | DATETIME | 可空 |

### 8.4 `files`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `original_name` | VARCHAR(255) | 仅用于显示 |
| `stored_name` | VARCHAR(100) | 服务器随机文件名 |
| `relative_path` | VARCHAR(500) | 受控目录下的相对路径 |
| `extension` | VARCHAR(32) | 规范化扩展名 |
| `mime_type` | VARCHAR(150) | 服务端识别结果 |
| `size_bytes` | INTEGER | 大小 |
| `sha256` | VARCHAR(64) | 文件哈希 |
| `uploader_id` | INTEGER | 外键 `users.id` |
| `is_deleted` | BOOLEAN | 默认 false |
| `created_at` | DATETIME | 必填 |

### 8.5 `artifacts`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `title` | VARCHAR(200) | 必填 |
| `summary` | VARCHAR(500) | 必填 |
| `content_markdown` | TEXT | 必填 |
| `author_id` | INTEGER | 外键 `users.id` |
| `status` | VARCHAR(32) | `DRAFT/PUBLISHED/ARCHIVED` |
| `published_at` | DATETIME | 可空 |
| `archived_at` | DATETIME | 可空 |
| `created_at` | DATETIME | 必填 |
| `updated_at` | DATETIME | 必填 |

`artifact_files` 使用 `artifact_id + file_id` 联合唯一约束，并包含 `sort_order`。

### 8.6 `comments`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `artifact_id` | INTEGER | 可空，外键 `artifacts.id` |
| `issue_id` | INTEGER | 可空，外键 `issues.id` |
| `author_id` | INTEGER | 外键 `users.id` |
| `content` | TEXT | 必填 |
| `status` | VARCHAR(16) | `VISIBLE/HIDDEN` |
| `created_at` | DATETIME | 必填 |
| `updated_at` | DATETIME | 必填 |

检查约束保证 `artifact_id` 与 `issue_id` 恰好一个非空。评论不做楼中楼。

### 8.7 `tasks`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `title` | VARCHAR(200) | 必填 |
| `description` | TEXT | 必填 |
| `creator_id` | INTEGER | 外键 `users.id` |
| `status` | VARCHAR(32) | `OPEN/COMPLETED/CLOSED` |
| `deadline_at` | DATETIME | 可空 |
| `completed_at` | DATETIME | 可空 |
| `closed_at` | DATETIME | 可空 |
| `created_at` | DATETIME | 必填 |
| `updated_at` | DATETIME | 必填 |

首版不建立任务参与者、成果或验收表。

### 8.8 `issues`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `title` | VARCHAR(200) | 必填 |
| `description` | TEXT | 必填 |
| `author_id` | INTEGER | 外键 `users.id` |
| `status` | VARCHAR(16) | `OPEN/CLOSED` |
| `closed_at` | DATETIME | 可空 |
| `created_at` | DATETIME | 必填 |
| `updated_at` | DATETIME | 必填 |

### 8.9 `competitions`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | INTEGER | 主键 |
| `title` | VARCHAR(200) | 必填 |
| `summary` | VARCHAR(500) | 必填 |
| `rules_markdown` | TEXT | 必填 |
| `start_at` | DATETIME | 必填 |
| `end_at` | DATETIME | 必填 |
| `created_by` | INTEGER | 外键 `users.id` |
| `created_at` | DATETIME | 必填 |
| `updated_at` | DATETIME | 必填 |

竞赛状态不保存，根据 `start_at`、`end_at` 与服务器当前时间计算为 `UPCOMING`、`ONGOING` 或 `ENDED`。

首版共 9 张主表：`users`、`user_sessions`、`files`、`artifacts`、`artifact_files`、`comments`、`tasks`、`issues`、`competitions`。不建立分类、标签、点赞、封面、工作台或审计日志相关表。

## 9. API 设计

统一前缀：`/api/v1`。列表响应统一包含 `items`、`page`、`page_size` 和 `total`；错误响应包含 `code`、`message` 和可选 `details`。

### 9.1 认证

```text
POST   /api/v1/auth/login
POST   /api/v1/auth/logout
GET    /api/v1/auth/me
POST   /api/v1/auth/change-password
GET    /api/v1/auth/csrf-token
```

### 9.2 探索

```text
GET    /api/v1/explore
```

知识库首版没有后端接口。

### 9.3 展品

```text
GET    /api/v1/artifacts
POST   /api/v1/artifacts
GET    /api/v1/artifacts/{id}
PATCH  /api/v1/artifacts/{id}
DELETE /api/v1/artifacts/{id}                 # 仅作者草稿
POST   /api/v1/artifacts/{id}/publish
POST   /api/v1/artifacts/{id}/archive
POST   /api/v1/artifacts/{id}/restore
GET    /api/v1/artifacts/{id}/comments
POST   /api/v1/artifacts/{id}/comments
```

### 9.4 任务

```text
GET    /api/v1/tasks
POST   /api/v1/tasks
GET    /api/v1/tasks/{id}
PATCH  /api/v1/tasks/{id}
POST   /api/v1/tasks/{id}/complete
POST   /api/v1/tasks/{id}/close
```

### 9.5 Issues

```text
GET    /api/v1/issues
POST   /api/v1/issues
GET    /api/v1/issues/{id}
PATCH  /api/v1/issues/{id}
POST   /api/v1/issues/{id}/close
POST   /api/v1/issues/{id}/reopen
GET    /api/v1/issues/{id}/comments
POST   /api/v1/issues/{id}/comments
```

### 9.6 评论

```text
DELETE /api/v1/comments/{id}                  # 作者删除自己的评论
POST   /api/v1/admin/comments/{id}/hide
POST   /api/v1/admin/comments/{id}/restore
```

### 9.7 竞赛

```text
GET    /api/v1/competitions
GET    /api/v1/competitions/{id}
POST   /api/v1/admin/competitions
PATCH  /api/v1/admin/competitions/{id}
DELETE /api/v1/admin/competitions/{id}
```

竞赛状态由查询响应根据开始和结束时间计算，没有状态修改接口。

### 9.8 文件和后台

```text
POST   /api/v1/files
GET    /api/v1/files/{id}/download
DELETE /api/v1/files/{id}                     # 仅未被引用的本人文件

GET    /api/v1/admin/users
POST   /api/v1/admin/users
PATCH  /api/v1/admin/users/{id}
POST   /api/v1/admin/users/{id}/reset-password
```

`/admin` 是单个前端页面，不要求所有管理接口共用同一路径；展品归档、任务或 Issue 关闭、评论隐藏等操作复用相应资源动作接口并由后端校验系统管理员角色。

### 9.9 状态码

| 状态码 | 用途 |
|---|---|
| `200/201/204` | 成功 |
| `400` | 业务输入不合法 |
| `401` | 未登录或会话失效 |
| `403` | 无权限 |
| `404` | 资源不存在或不可见 |
| `409` | 状态冲突或资源仍被引用 |
| `413` | 文件过大 |
| `415` | 不支持的文件类型 |
| `422` | 字段校验失败 |

## 10. 文件处理

### 10.1 附件范围

首版只允许展品关联附件。任务、Issue 和竞赛先使用 Markdown 文本，不增加附件关联表。

- 单文件最大 50 MB。
- 每条展品最多 10 个附件。
- 文件内容不写入 SQLite BLOB。
- 文件只能通过受控下载接口访问。
- 平台不执行上传内容，不提供 HTML 或脚本预览。

允许扩展名：

```text
文档与数据：pdf, docx, xlsx, pptx, md, txt, csv, json
图片：png, jpg, jpeg, webp
代码与配置：py, js, ts, vue, sql, yaml, yml, toml
Notebook：ipynb
压缩包：zip
```

禁止扩展名：

```text
exe, msi, dll, bat, cmd, com, ps1, sh, html, htm
```

### 10.2 存储路径

```text
storage/uploads/{yyyy}/{mm}/{uuid}.{normalized_extension}
```

原始文件名仅存入数据库用于显示，不能参与服务器路径拼接。

### 10.3 上传流程

1. 校验登录和 CSRF。
2. 校验请求大小、文件大小和附件数量。
3. 校验扩展名、MIME 和文件头。
4. 生成随机文件名并写入临时文件。
5. 计算 SHA-256。
6. 移动到受控目录并保存元数据。
7. 保存展品时建立附件引用。
8. 失败时清理本次临时文件。

## 11. 认证与安全

### 11.1 最小登录方案

- 管理员创建账号，不开放注册。
- 用户名和密码登录。
- 用户可自行修改密码，管理员可重置密码。
- 密码使用安全的单向哈希算法。
- 密码只校验 8–128 位，不要求大小写、数字或特殊字符组合。
- 服务端会话配合 HttpOnly Cookie。
- Cookie 使用 `SameSite=Lax`，生产环境启用 `Secure`。
- 数据库只保存会话令牌哈希。
- 退出、改密和停用账号时撤销相关会话。
- 前端不在 `localStorage` 或 `sessionStorage` 保存访问令牌。

### 11.2 CSRF 与输入输出

- 正式环境前后端同源。
- 登录请求不要求 CSRF Token；其他已登录写请求携带 `X-CSRF-Token`。
- CSRF Token 使用服务端密钥和当前会话令牌派生并校验，不单独持久化。
- 首版不校验 Origin/Referer，不增加来源白名单或自动重试等复杂策略。
- Markdown 渲染后严格清洗，禁止原始 HTML、事件属性、危险 URL 和任意 iframe。
- API 错误不暴露堆栈、数据库路径和内部配置。
- 日志不记录密码、Cookie、会话令牌、CSRF Token 或文件正文。

## 12. SQLite 与部署

### 12.1 SQLite 配置

- `PRAGMA foreign_keys = ON`。
- 开启 WAL。
- 设置合理的 busy timeout。
- 使用短事务。
- 数据库位于服务器本地磁盘，不使用 SMB、NAS 或 NFS 共享目录。
- 正式环境运行单个 FastAPI 应用实例和单个 Uvicorn Worker。

### 12.2 部署结构

```text
浏览器
  -> FastAPI
       -> REST API
       -> Vue 生产静态文件
       -> SQLite 本地文件
       -> 本地上传目录
```

Docker Compose 首版只管理一个应用服务。SQLite 数据目录和上传目录映射到宿主机本地持久化目录，不能保存在容器临时层。

### 12.3 配置项

`.env.example` 至少描述：

```text
APP_ENV
APP_NAME
APP_SECRET_KEY
DATABASE_URL
DATA_DIR
UPLOAD_DIR
MAX_UPLOAD_SIZE_MB
MAX_ATTACHMENTS_PER_ARTIFACT
ALLOWED_UPLOAD_EXTENSIONS
SESSION_COOKIE_NAME
SESSION_TTL_HOURS
FRONTEND_ORIGIN
LOG_LEVEL
INITIAL_ADMIN_USERNAME
```

初始密码、密钥和正式数据库文件不得提交 Git。

## 13. Git 与工程基线

项目使用单一 Git 仓库：

```text
MkAIHub/
  frontend/
  backend/
  docs/
  deploy/
  data/       # 运行目录，不提交
  storage/    # 运行目录，不提交
```

- `main` 保存可运行的集成基线。
- 功能开发使用短生命周期分支。
- 必须提交 `uv.lock`、`package-lock.json`、Alembic 迁移和文档。
- 不提交 `.env`、`.venv`、`node_modules`、前端构建产物、SQLite 数据和上传文件。
- 当前目录已在批次 0 初始化为单一 Git 仓库，默认分支为 `main`；提交和远端推送仍需单独授权。

常用开发命令以工程生成后的实际脚本为准，目标形式为：

```text
uv sync --locked
uv run alembic upgrade head
uv run pytest
uv run uvicorn app.main:app --reload

npm ci
npm run dev
npm run type-check
npm run test
npm run build
```

## 14. 开发批次

### 批次 0：工程基础

交付：

- 初始化 Git、忽略规则和目录结构。
- 用 uv 初始化 Python 3.13 后端并生成 `uv.lock`。
- 使用系统 Node.js、npm 初始化 Vue 工程并生成 `package-lock.json`。
- 配置 FastAPI、SQLAlchemy、Alembic、SQLite、日志和统一错误响应。
- 建立 Vue 主布局、主题变量和基础 API 客户端。
- 增加 `/api/health`。

退出条件：

- `uv sync --locked`、后端测试入口可运行。
- `npm ci`、类型检查和生产构建可运行。
- Alembic 可从空数据库升级。
- 前后端开发环境可启动。

### 批次 1：认证与六模块导航

交付：

- 用户和会话模型。
- 登录、退出、当前用户、改密和管理员用户维护。
- 主布局、六个一级导航、路由守卫和空状态骨架页。
- 单个管理页和角色控制。

退出条件：

- 登录后可以进入六个真实路由。
- 刷新后会话有效。
- 停用用户无法登录。
- 普通员工无法进入或调用管理功能。

### 批次 2：展品核心、知识库与探索

交付：

- 展品、文件和评论模型。
- 展品 CRUD、状态机、上传和下载。
- 展品列表、详情、创建和编辑页面。
- 知识库占位页面。
- 探索页的六模块入口和最新展品区域。

退出条件：

- 员工可发布一条带附件的展品。
- 其他员工可搜索、查看、下载和评论。
- 知识库路由可访问且不请求独立业务 API。
- 非作者不能修改正文。

### 批次 3：任务与 Issues

交付：

- 任务基础 CRUD 和状态动作。
- 任务列表、详情、创建和编辑页面。
- Issues 创建、编辑、评论、关闭和重开。
- Issues 列表和详情页面。

退出条件：

- 任务创建后直接开放，可完成“开放—完成/关闭”的基础流程。
- 不存在参与者、成果或验收入口。
- Issue 可完成“发起—讨论—关闭—重开”流程。

### 批次 4：竞赛与最小管理页

交付：

- 竞赛列表、详情和管理员维护。
- 各业务列表的“只看我的”筛选。
- 单个 `/admin` 页面及用户、内容、竞赛三个页签。
- 展品、任务、Issues 和评论的必要管理动作。

退出条件：

- 普通员工可查看竞赛但不能维护。
- 管理员可以维护竞赛基本信息，状态随时间自动计算。
- “只看我的”正确筛选当前用户资源。
- 关键管理动作写入后端结构化日志。

### 批次 5：部署与交付

交付：

- Vue 生产构建由 FastAPI 托管。
- Linux 单机 Docker Compose 配置。
- 数据和上传目录持久化。
- 数据库与文件备份脚本和恢复说明。
- 初始化说明、演示数据和回归测试。

退出条件：

- 新环境可以按文档初始化。
- 容器重启后数据和文件完整。
- 完成一次实际备份和恢复验证。
- 核心浏览器流程通过。

## 15. 测试方案

### 15.1 后端测试

Pytest 覆盖：

- 登录、退出、过期、改密和会话撤销。
- 两种角色和资源归属权限。
- 展品、任务和 Issue 状态转换，以及竞赛时间状态计算。
- 草稿可见性和已归档内容过滤。
- 评论创建、删除、隐藏和关闭 Issue 后拒绝评论。
- 文件大小、数量、类型、路径和引用校验。
- 探索页只返回最近发布的 6 条展品。
- 管理动作结构化日志。

### 15.2 前端测试

Vitest 覆盖：

- API 错误处理。
- 权限辅助函数。
- 状态值到文案和操作按钮的映射。
- 展品、任务、Issue 和竞赛表单校验。
- 知识库占位页渲染。
- 公共评论和附件组件。

### 15.3 浏览器流程

Playwright 至少覆盖：

1. 管理员创建用户，员工登录并修改密码。
2. 员工创建、发布和查看带附件展品。
3. 知识库导航和占位页面可以正常访问。
4. 员工创建任务并完成或关闭。
5. 两名员工完成 Issue 发起、评论、关闭和重开。
6. 管理员创建竞赛，普通员工只能查看。
7. 普通员工访问管理页面和接口被拒绝。

## 16. 首版验收清单

### 16.1 功能

- [ ] 六个一级导航均有可访问的真实页面。
- [ ] 探索页显示六个模块入口和最近发布的 6 条展品。
- [ ] 员工可创建、编辑、发布、归档自己的展品。
- [ ] 展品支持附件和评论，不包含类型、分类、标签、点赞或封面。
- [ ] 知识库只提供占位页，不建立数据表或 API。
- [ ] 员工可创建和维护自己的基础任务。
- [ ] 员工可发起、评论、关闭和重开 Issue。
- [ ] 管理员可维护竞赛，员工可查看。
- [ ] 各业务列表支持“只看我的”。
- [ ] 单个管理页具备用户、内容和竞赛维护能力。

### 16.2 权限

- [ ] 未登录用户不能访问业务页面和 API。
- [ ] 普通员工不能调用管理 API。
- [ ] 非作者不能编辑他人的展品和任务。
- [ ] 草稿不向其他普通员工暴露。
- [ ] 普通员工不能维护竞赛。
- [ ] 下载接口检查登录和资源可见性。
- [ ] 状态字段不能通过普通更新接口任意修改。

### 16.3 数据与运维

- [ ] Alembic 可从空数据库创建全部结构。
- [ ] SQLite 已开启外键、WAL 和锁等待设置。
- [ ] 上传文件不进入数据库 BLOB。
- [ ] 超过 50 MB、超过 10 个附件或禁止类型被拒绝。
- [ ] 数据目录和上传目录持久化。
- [ ] 备份同时覆盖数据库和文件。
- [ ] 已执行并记录一次恢复验证。
- [ ] 日志不包含密码、会话令牌或文件正文。

### 16.4 质量

- [ ] 后端测试通过。
- [ ] 前端类型检查和单元测试通过。
- [ ] 核心 Playwright 流程通过。
- [ ] 桌面端主要页面完成视觉检查。
- [ ] API 文档与实际接口一致。

## 17. 风险与控制

| 风险 | 首版控制 |
|---|---|
| 六个模块导致范围膨胀 | 展品做完整闭环，其他模块严格按骨架深度实现 |
| 知识库边界尚未明确 | 首版只保留占位页，不提前建立或复制数据模型 |
| 任务流程提前复杂化 | 不建参与、成果和验收数据结构 |
| 竞赛演变为大型运营系统 | 首版仅管理员维护信息、员工查看 |
| SQLite 并发写入限制 | 单实例、短事务、WAL 和锁等待设置 |
| 上传危险文件 | 白名单、大小限制、随机路径、受控下载 |
| Markdown XSS | 严格清洗并禁止原始 HTML |
| 权限遗漏 | 服务层集中校验并编写越权测试 |
| 数据和文件备份不一致 | 一致性备份流程和实际恢复演练 |
| 原型样式难维护 | 提取主题变量并按组件迁移 |

## 18. 后续演进接口

首版只保留合理扩展边界，不创建空业务表：

- 任务后续可增加参与者、成果、验收和展品来源关系。
- 竞赛后续可增加报名、队伍、作品、评审和排行。
- 知识库后续根据实际需求建设独立内容模型，或通过显式关系关联展品，再增加文档解析、索引和 RAG；二者不依赖展品类型耦合。
- 文件服务后续可从本地目录迁移到对象存储。
- 数据库后续可通过 SQLAlchemy 和 Alembic 迁移 PostgreSQL。
- 认证后续可通过适配层接入公司 SSO。

以上扩展均不属于首版验收范围。

## 19. 编码启动条件

以下基础决策已经明确：

- 公司内部轻量级 AI 分享平台。
- 六个一级模块及其首版深度。
- 展品为核心，不设置类型、分类或标签，只使用关键词检索。
- FastAPI、Python 3.13、uv、SQLite。
- Vue 3、TypeScript、Vite、Element Plus、系统 Node.js、npm。
- 单一 Git 仓库。
- Linux + Docker Compose 单机部署。
- 最小账号密码登录方案。
- 本地文件存储和附件限制。

所有影响首版范围的产品问题均已确认，当前不存在阻塞工程初始化的业务决策。
