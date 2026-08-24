# MkAIHub 第二阶段批次7数据契约

> 文档状态：批次7实施基线（批次8/9的编码依据）
> 版本：v1.0
> 更新日期：2026-08-24
> 前置文档：[MkAIHub 第二阶段闭环实施方案](./MkAIHub第二阶段闭环实施方案.md)

## 1. 文档目的

本文档是第二阶段批次7的交付物：把《第二阶段闭环实施方案》第 6.2 节的业务决策转为明确结论，并固化状态转换表、权限矩阵、数据表契约、接口契约、迁移方案和测试清单。

批次8和批次9按本文件实现。本文件与《第二阶段闭环实施方案》冲突时，以本文件为准；本文件未覆盖的边界问题回到该方案的原则处理。

## 2. 已确认业务决策

以下 8 项决策于 2026-08-24 确认，全部采纳《第二阶段闭环实施方案》第 6.2 节的建议默认方案。

| 决策项 | 确认结论 | 主要影响 |
|---|---|---|
| 任务参与方式 | 员工直接领取，任务发布人无需审批 | 参与接口即领即得，无审批状态 |
| 普通任务参与人数 | 允许多人分别参与和提交，发布人可选一个或多个验收成果 | 参与表无人数上限；任务完成条件为存在至少一条已验收提交 |
| 提交展品可见性 | 只能提交 `PUBLISHED` 状态的展品，不新增受限可见状态 | 展品状态、查询、下载和评论权限完全复用现有规则 |
| 竞赛参与方式 | 首期只支持个人报名 | 报名表以用户为单位，不做队伍 |
| 竞赛评审人 | 系统管理员直接评审，不建评委配置表 | 评审接口仅 `SYSTEM_ADMIN` 可调用 |
| 结果可见性 | 管理员确认后一次性发布正式结果，发布后排名冻结 | 发布前评审分数对参赛人不可见；发布后读快照 |
| 必做任务缺失 | 不进入正式排名；选做任务未提交按 0 分 | 结果发布时先判定资格，再计算加权总分 |
| 并列名次 | 同分同名次，不用提交早晚打破并列 | 采用标准竞赛排名（1、1、3） |

补充确认（实施方案 8.1、7.4 节遗留项）：

- 管理员纠错：允许管理员把 `CLOSED` 的任务重开（回到 `OPEN` 或 `IN_PROGRESS`）；`COMPLETED` 为终态不可重开。所有纠错动作记录结构化日志。
- 报名取消：竞赛结束时间之前允许取消，但本人已在该竞赛任何任务下产生提交时不可取消；取消后可在窗口内重新报名。
- 竞赛物理删除：保留现有删除接口，但只要存在竞赛任务、报名或结果记录即拒绝删除（服务层预检 + 外键 RESTRICT 兜底）。

## 3. 数据契约

命名、时间与现有约定一致：字段 snake_case，时间使用 `DateTime(timezone=True)` 存 UTC；所有外键 `ON DELETE RESTRICT`（SQLite 已开启 `PRAGMA foreign_keys`）；得分列用 `Numeric`，服务层统一用 `Decimal` 计算并按定义的小数位 `ROUND_HALF_UP` 落库，避免浮点漂移。

### 3.1 `tasks` 表变更

新增列（批次9迁移）：

| 列 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `competition_id` | Integer | 可空，外键 `competitions.id` RESTRICT | 空 = 普通独立任务；非空 = 竞赛任务 |
| `competition_required` | Boolean | 可空 | 竞赛任务是否必做 |
| `competition_sort_order` | Integer | 可空 | 竞赛详情中的显示顺序 |
| `competition_max_score` | Numeric(6,2) | 可空 | 该任务原始评分上限 |
| `competition_weight` | Numeric(5,2) | 可空 | 计入竞赛总分的权重 |

复用现有列：

- `deadline_at`：竞赛任务的提交截止时间。为空时继承竞赛 `end_at`；非空时必须不晚于竞赛 `end_at`（服务层校验，因为跨表无法用 CHECK 表达）。普通任务的 `deadline_at` 仍为展示性字段，不参与状态机。
- `status`：CHECK 约束从 `('OPEN','COMPLETED','CLOSED')` 扩展为 `('OPEN','IN_PROGRESS','REVIEWING','COMPLETED','CLOSED')`（批次8迁移，SQLite 需 batch 模式重建表）。

新增约束与索引：

```text
CHECK (competition_id IS NULL OR (competition_max_score > 0 AND competition_weight > 0))
  名称 ck_tasks_competition_score
CHECK (
  competition_id IS NOT NULL
  OR (
    competition_required IS NULL AND competition_sort_order IS NULL
    AND competition_max_score IS NULL AND competition_weight IS NULL
  )
)
  名称 ck_tasks_competition_fields
INDEX ix_tasks_competition (competition_id, competition_sort_order)
```

语义规则：

- 一项任务最多属于一个竞赛；竞赛任务通过 `POST /api/v1/admin/competitions/{id}/tasks` 创建，普通创建接口不接收 `competition_id`。
- 竞赛产生 `REGISTERED` 报名或任何任务提交后，任务的必做/选做、最高分、权重不可修改，任务不可删除；排序仍可调整（纯展示）。违规返回 `COMPETITION_CONFIG_LOCKED`。
- 竞赛任务不提供从竞赛移除的接口；`DRAFT` 竞赛下的任务可删除（见 4.6 节）。

### 3.2 `competitions` 表变更

新增列（批次9迁移）：

| 列 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `status` | String(32) | 非空，默认 `'PUBLISHED'`，CHECK `('DRAFT','PUBLISHED','RESULT_PUBLISHED','ARCHIVED')` | 运营状态，与按时间计算的展示状态并存 |

- 运营状态回答"内容是否发布、结果是否公开"，时间状态（`UPCOMING/ONGOING/ENDED`）继续由 `start_at`/`end_at` 派生，两者不混用。
- 升级后存量竞赛 `status='PUBLISHED'`，保持现有可见性；此后管理员新建的竞赛初始为 `DRAFT`，仅管理员可见，需显式发布。
- 状态流转的审计依赖管理动作结构化日志（`competition.publish`、`competition.publish_results`、`competition.archive`），不加状态时间列。

### 3.3 新表 `task_participants`（批次8迁移）

| 列 | 类型 | 约束 |
|---|---|---|
| `id` | Integer | 主键 |
| `task_id` | Integer | 非空，外键 `tasks.id` RESTRICT |
| `user_id` | Integer | 非空，外键 `users.id` RESTRICT |
| `status` | String(32) | 非空，默认 `'ACTIVE'`，CHECK `('ACTIVE','LEFT')` |
| `joined_at` | DateTime(tz) | 非空 |
| `left_at` | DateTime(tz) | 可空 |
| `created_at` / `updated_at` | DateTime(tz) | 非空，现有惯例 |

约束与索引：

```text
UNIQUE (task_id, user_id)          uq_task_participants_task_user
INDEX  ix_task_participants_user (user_id, status)   -- 「我参与的」查询
```

- 同一用户在同一任务只有一条参与记录；退出后重新参与复用同一行，状态翻回 `ACTIVE` 并更新 `joined_at`。
- 任务不提供物理删除接口（现有 API 无删除，保持不变），外键 RESTRICT 兜底。

### 3.4 新表 `task_submissions`（批次8迁移）

| 列 | 类型 | 约束 |
|---|---|---|
| `id` | Integer | 主键 |
| `task_id` | Integer | 非空，外键 `tasks.id` RESTRICT |
| `participant_id` | Integer | 非空，外键 `task_participants.id` RESTRICT |
| `artifact_id` | Integer | 非空，外键 `artifacts.id` RESTRICT |
| `round_no` | Integer | 非空，CHECK `round_no >= 1` |
| `note` | Text | 可空，提交说明 |
| `status` | String(32) | 非空，默认 `'SUBMITTED'`，CHECK `('SUBMITTED','REVISION_REQUIRED','ACCEPTED','REJECTED')` |
| `is_current` | Boolean | 非空，默认 `true` |
| `submitted_at` | DateTime(tz) | 非空 |
| `revision_requested_at` | DateTime(tz) | 可空 |
| `decided_at` | DateTime(tz) | 可空 |
| `decider_id` | Integer | 可空，外键 `users.id` RESTRICT，验收/退回/拒绝执行人 |
| `decision_note` | Text | 可空，处理意见 |
| `created_at` / `updated_at` | DateTime(tz) | 非空，现有惯例 |

约束与索引：

```text
UNIQUE (participant_id, round_no)  uq_task_submissions_round
INDEX  ix_task_submissions_task_status (task_id, status)
INDEX  ix_task_submissions_participant (participant_id)
INDEX  ix_task_submissions_artifact (artifact_id)   -- 展品反向来源查询
```

- `is_current` 标记该参与人在该任务下的当前最终有效提交；每名参与人每任务至多一条 `is_current=true`（服务层保证）。提交新轮次时把旧轮次置 `false`，历史记录不覆盖、不删除。
- 展品被任何提交引用后不可物理删除：展品删除接口本就仅限草稿，提交又要求已发布展品，外键 RESTRICT 作为兜底；归档不受影响。
- 竞赛任务与普通任务共用本表；竞赛提交的状态恒为 `SUBMITTED`（退回/验收/拒绝动作不适用于竞赛提交，见 4.4 节）。

### 3.5 新表 `competition_registrations`（批次9迁移）

| 列 | 类型 | 约束 |
|---|---|---|
| `id` | Integer | 主键 |
| `competition_id` | Integer | 非空，外键 `competitions.id` RESTRICT |
| `user_id` | Integer | 非空，外键 `users.id` RESTRICT |
| `status` | String(32) | 非空，默认 `'REGISTERED'`，CHECK `('REGISTERED','CANCELLED')` |
| `registered_at` | DateTime(tz) | 非空 |
| `cancelled_at` | DateTime(tz) | 可空 |
| `created_at` / `updated_at` | DateTime(tz) | 非空，现有惯例 |

约束与索引：

```text
UNIQUE (competition_id, user_id)   uq_competition_registrations_competition_user
INDEX  ix_competition_registrations_user (user_id)
```

- 同一用户在同一竞赛只有一条报名记录；取消后重新报名复用同一行，状态翻回 `REGISTERED` 并更新 `registered_at`。

### 3.6 新表 `competition_reviews`（批次9迁移）

| 列 | 类型 | 约束 |
|---|---|---|
| `id` | Integer | 主键 |
| `task_submission_id` | Integer | 非空，唯一，外键 `task_submissions.id` RESTRICT |
| `reviewer_id` | Integer | 非空，外键 `users.id` RESTRICT |
| `raw_score` | Numeric(6,2) | 非空 |
| `comment` | Text | 可空，评语 |
| `reviewed_at` | DateTime(tz) | 非空 |
| `created_at` / `updated_at` | DateTime(tz) | 非空，现有惯例 |

- 一条提交唯一一条正式评审记录：`UNIQUE (task_submission_id)`；修改评分即更新该行（重评审）并记录管理日志。首期只有系统管理员评审，不设 `is_official` 恒真列和评委表，未来引入多评委时再扩展。
- `0 <= raw_score <= 任务的 competition_max_score` 跨表校验由服务层执行（CHECK 无法引用其他表）。
- 只允许对 `is_current=true` 的提交建评审；参赛人在结果发布前不可见任何评审数据。

### 3.7 新表 `competition_results`（批次9迁移）

| 列 | 类型 | 约束 |
|---|---|---|
| `id` | Integer | 主键 |
| `competition_id` | Integer | 非空，外键 `competitions.id` RESTRICT |
| `registration_id` | Integer | 非空，外键 `competition_registrations.id` RESTRICT |
| `total_score` | Numeric(7,4) | 非空 |
| `rank` | Integer | 非空，CHECK `rank >= 1` |
| `award` | String(200) | 可空，奖项名称 |
| `published_by` | Integer | 非空，外键 `users.id` RESTRICT |
| `published_at` | DateTime(tz) | 非空 |
| `created_at` / `updated_at` | DateTime(tz) | 非空，现有惯例 |

约束与索引：

```text
UNIQUE (competition_id, registration_id)  uq_competition_results_competition_registration
INDEX  ix_competition_results_rank (competition_id, rank)
```

- 结果发布时从各任务最终有效提交的正式评审计算并写入快照；此后列表与详情读取快照，不随数据修正自动漂移。纠错只能由管理员重新发布（整体重算并替换快照，记录管理日志）。
- 不进入正式排名的参赛人不写结果行（表示"未排名"），不用哨兵值。

### 3.8 外键与删除规则汇总

| 被引用表 | 引用方 | 删除规则 |
|---|---|---|
| `competitions` | `tasks.competition_id`、`competition_registrations`、`competition_results` | RESTRICT；存在任一引用时删除接口返回 `COMPETITION_HAS_REFERENCES` |
| `tasks` | `task_participants`、`task_submissions` | RESTRICT；任务无物理删除接口，维持现状 |
| `task_participants` | `task_submissions.participant_id` | RESTRICT |
| `task_submissions` | `competition_reviews.task_submission_id` | RESTRICT |
| `artifacts` | `task_submissions.artifact_id` | RESTRICT；被引用展品不可物理删除（草稿删除接口 + 服务层预检） |
| `users` | 各表执行人/参与人列 | RESTRICT；首版无用户删除，无实际影响 |

## 4. 状态机

每个动作都有执行人、前置状态、后置状态和失败条件；状态变更全部集中在服务层，路由只调用服务。时间比较一律使用服务器 UTC 时间。

### 4.1 普通独立任务（`competition_id` 为空）

状态：`OPEN → IN_PROGRESS → REVIEWING → COMPLETED / CLOSED`。

| 动作 | 执行人 | 前置状态 | 后置状态 | 失败条件 |
|---|---|---|---|---|
| 创建任务（现有接口） | 登录员工 | — | `OPEN` | 不变 |
| 编辑任务（现有接口） | 发布人 | 非终态（`OPEN` / `IN_PROGRESS` / `REVIEWING`） | 不变 | 终态 → `TASK_STATE_CONFLICT` |
| 领取任务 | 任何登录员工 | 非终态（`OPEN` / `IN_PROGRESS` / `REVIEWING`） | 首个有效参与把 `OPEN` 翻为 `IN_PROGRESS`；`REVIEWING` 下领取不改变任务状态 | 任务终态 → `TASK_STATE_CONFLICT`；已有 `ACTIVE` 参与 → `TASK_ALREADY_PARTICIPATED` |
| 退出参与 | 参与人本人 | 任务非终态且本人无任何提交 | 参与行 `LEFT`，记 `left_at`；无其他 `ACTIVE` 参与人时不回退任务状态 | 已有提交 → `TASK_SUBMISSION_EXISTS`；任务终态 → `TASK_STATE_CONFLICT` |
| 提交/重新提交 | `ACTIVE` 参与人 | 任务非终态，且无当前提交或当前提交为 `REVISION_REQUIRED`；展品为本人 `PUBLISHED` 展品 | 新提交行 `SUBMITTED` 且 `is_current=true`，旧当前行置 `false`；任务进入 `REVIEWING` | 非参与人 → `FORBIDDEN`；任务终态 → `TASK_STATE_CONFLICT`；当前提交为 `SUBMITTED` 或 `ACCEPTED` → `SUBMISSION_ALREADY_PENDING`；展品非本人或非 `PUBLISHED` → `ARTIFACT_NOT_SUBMITTABLE` |
| 退回修改 | 发布人或管理员 | 当前提交为 `SUBMITTED` | 提交 `REVISION_REQUIRED`，记 `revision_requested_at`、`decider_id`、`decision_note` | 提交已终态或非当前 → `SUBMISSION_STATE_CONFLICT`；无权限 → `FORBIDDEN` |
| 验收 | 发布人或管理员 | 当前提交为 `SUBMITTED` 或 `REVISION_REQUIRED` | 提交 `ACCEPTED`，记 `decided_at`、`decider_id`、`decision_note` | 同上 |
| 拒绝（不采用） | 发布人或管理员 | 当前提交为 `SUBMITTED` 或 `REVISION_REQUIRED` | 提交 `REJECTED`，记 `decided_at` 等 | 同上 |
| 完成任务（现有接口扩展） | 发布人 | `IN_PROGRESS` / `REVIEWING` 且存在至少一条 `ACCEPTED` 提交 | `COMPLETED`，记 `completed_at` | 无已验收提交 → `TASK_NO_ACCEPTED_RESULT`；非 `IN_PROGRESS`/`REVIEWING` → `TASK_STATE_CONFLICT`；非发布人 → `FORBIDDEN` |
| 关闭任务（现有接口扩展） | 发布人或管理员 | 非终态（`OPEN` / `IN_PROGRESS` / `REVIEWING`） | `CLOSED`，记 `closed_at` | 终态（`COMPLETED` / `CLOSED`）→ `TASK_STATE_CONFLICT` |
| 重开任务（新增） | 管理员 | `CLOSED` | 存在 `ACTIVE` 参与人 → `IN_PROGRESS`，否则 `OPEN`；记录管理日志 | `COMPLETED` 不可重开 → `TASK_STATE_CONFLICT` |

`REVIEWING` 与 `IN_PROGRESS` 的重算规则（服务层在每个提交动作与决定动作后执行）：

- 存在 `is_current=true` 且状态为 `SUBMITTED` 或 `REVISION_REQUIRED` 的提交 → 任务为 `REVIEWING`。
- 否则若任务未终态且存在 `ACTIVE` 参与人 → `IN_PROGRESS`；无参与人 → `OPEN`。

提交动作的目标只能是 `is_current=true` 且未终态的行；对历史轮次执行动作返回 `SUBMISSION_STATE_CONFLICT`。竞赛任务提交不适用本节动作，见 4.4。

### 4.2 竞赛任务（`competition_id` 非空）

状态仅 `OPEN ↔ CLOSED`，由管理员切换（复用现有 close 接口语义 + 重开）；`IN_PROGRESS` / `REVIEWING` / `COMPLETED` 永不出现在竞赛任务上：

- 对竞赛任务调用 `complete` → `TASK_STATE_CONFLICT`。
- 竞赛任务是否可提交由父竞赛状态（`PUBLISHED`）、任务状态（`OPEN`）和截止时间（`deadline_at`，为空取竞赛 `end_at`）共同决定；个人完成情况只记录在提交中。
- 管理员停用（`CLOSED`）后不再接受提交，可重开回 `OPEN`。

### 4.3 竞赛报名

| 动作 | 执行人 | 前置条件 | 后置状态 | 失败条件 |
|---|---|---|---|---|
| 报名 | 任何登录员工（含管理员以个人身份） | 竞赛 `PUBLISHED` 且当前时间在 `[start_at, end_at)` | `REGISTERED`，记 `registered_at` | 竞赛 `DRAFT`（对员工不可见，404）或 `ARCHIVED` / `RESULT_PUBLISHED` → `COMPETITION_STATE_CONFLICT`；窗口外 → `COMPETITION_REGISTRATION_CLOSED`；已有 `REGISTERED` → `COMPETITION_ALREADY_REGISTERED` |
| 取消报名 | 报名人本人 | `REGISTERED`，当前时间早于 `end_at`，且本人未在该竞赛任何任务下提交过 | `CANCELLED`，记 `cancelled_at` | 已有提交 → `COMPETITION_SUBMISSION_EXISTS`；已过 `end_at` → `COMPETITION_REGISTRATION_CLOSED` |

取消后重新报名在窗口内允许（行状态翻回 `REGISTERED`）。

### 4.4 竞赛任务提交与评审

| 动作 | 执行人 | 前置条件 | 后置状态 | 失败条件 |
|---|---|---|---|---|
| 提交/重新提交 | `ACTIVE` 参与人 | 有效 `REGISTERED` 报名；竞赛 `PUBLISHED`；当前时间在 `[start_at, 任务截止时间)`；任务 `OPEN`；展品为本人 `PUBLISHED` 展品 | 新提交行 `SUBMITTED` 且 `is_current=true`，旧当前行置 `false`，`round_no` 递增 | 无有效报名 → `COMPETITION_REGISTRATION_REQUIRED`；未开始或已截止 → `COMPETITION_SUBMISSION_CLOSED`；竞赛非 `PUBLISHED` 或任务停用 → `COMPETITION_STATE_CONFLICT`；展品不合规 → `ARTIFACT_NOT_SUBMITTABLE` |
| 评审 | 管理员 | 提交 `is_current=true`；竞赛 `PUBLISHED`（含已结束未发布结果）；`0 <= raw_score <= competition_max_score` | upsert `competition_reviews` 行 | 非管理员 → `FORBIDDEN`；目标非当前提交 → `SUBMISSION_STATE_CONFLICT`；分数越界 → `REVIEW_SCORE_INVALID` |
| 退回/验收/拒绝 | — | 不适用 | — | 竞赛提交不接受这些动作 → `SUBMISSION_STATE_CONFLICT` |

- 多轮提交不设轮次上限，截止时间过后自然锁定（提交时校验时间，不依赖后台任务）。
- 评审针对当前有效提交；参赛人在截止前重新提交会使旧提交失去当前资格，旧评审保留在旧行上但不参与计分，管理员需对新当前提交补评审（管理页展示评审进度）。

### 4.5 竞赛运营状态

| 动作 | 执行人 | 前置状态 | 后置状态 | 失败条件 |
|---|---|---|---|---|
| 创建竞赛（现有接口，行为变更） | 管理员 | — | `DRAFT` | 不变 |
| 发布 | 管理员 | `DRAFT`，已配置至少一项竞赛任务 | `PUBLISHED` | 无任务 → `COMPETITION_NO_TASKS`；非 `DRAFT` → `COMPETITION_STATE_CONFLICT` |
| 修改基本信息（现有接口） | 管理员 | `DRAFT` 可改全部；`PUBLISHED` 可改 `title`/`summary`/`rules_markdown`/`end_at`（`end_at` 不得早于 `start_at`） | 不变 | `RESULT_PUBLISHED` / `ARCHIVED` → `COMPETITION_STATE_CONFLICT` |
| 配置任务（增/改/删） | 管理员 | `DRAFT` 自由；`PUBLISHED` 且无报名无提交时可增改；产生报名或提交后仅 `competition_sort_order` 可改，删除仅限 `DRAFT` | — | 锁定后改计分配置或删任务 → `COMPETITION_CONFIG_LOCKED` |
| 发布结果 | 管理员 | `PUBLISHED`，当前时间不早于 `end_at`，所有合格参赛人的当前有效提交均有正式评审 | `RESULT_PUBLISHED`，写入 `competition_results` 快照 | 未结束 → `COMPETITION_NOT_ENDED`；评审不全 → `COMPETITION_REVIEWS_INCOMPLETE` |
| 重新发布（纠错） | 管理员 | `RESULT_PUBLISHED` | 重算并整体替换快照，记录管理日志 | 其他状态 → `COMPETITION_STATE_CONFLICT` |
| 归档 | 管理员 | `RESULT_PUBLISHED`；或 `PUBLISHED` 且无任何 `REGISTERED` 报名（作废场景） | `ARCHIVED` | 有报名但未发布结果 → `COMPETITION_STATE_CONFLICT` |
| 删除（现有接口保留） | 管理员 | 无任务、无报名、无结果 | 行删除 | 存在引用 → `COMPETITION_HAS_REFERENCES` |

可见性：`DRAFT` 竞赛仅管理员可见（员工访问列表不返回、访问详情返回 404）；`PUBLISHED` 及之后对全部登录用户可见。

## 5. 权限与可见性矩阵

所有校验在服务层执行；前端只负责隐藏入口。

| 资源/动作 | 普通员工（非相关） | 参与人/报名人 | 任务发布人 | 系统管理员 |
|---|---|---|---|---|
| 任务列表/详情 | 可见（不变） | 可见 | 可见 | 可见 |
| 竞赛列表/详情 | `DRAFT` 不可见（404），其余可见 | 同左 | 同左 | 全部可见 |
| 领取任务 | 可以（未完成、未关闭的任务） | 可以 | 可以（他人任务） | 可以 |
| 提交成果 | 仅本人 `ACTIVE` 参与的任务；竞赛任务还需有效报名 | 可以 | 参与时同员工 | 不代替员工提交 |
| 退回/验收/拒绝 | 不可以 | 不可以 | 可以（自己发布的普通任务） | 可以（异常处理），记录日志 |
| 任务参与人列表 | 可见（显示名与时间） | 同左 | 同左 | 同左 |
| 普通任务提交列表 | 仅见任务 `COMPLETED` 后的 `ACCEPTED` 提交（最终成果展示） | 见本人全部轮次 | 见全部 | 见全部 |
| 竞赛任务提交列表 | 不可见他人提交 | 见本人全部轮次；评分仅结果发布后可见 | — | 见全部及评分 |
| 报名/取消报名 | 可以（窗口内） | — | — | 可以以个人身份报名 |
| 竞赛报名列表 | 详情页仅见报名数 | 同左 | 同左 | 完整列表 |
| 竞赛任务配置/评审/发布结果/归档 | 不可以 | 不可以 | 不可以 | 仅管理员，记录日志 |
| 排行榜 | 仅 `RESULT_PUBLISHED` 后可见 | 同左 | 同左 | 同左 |
| 附件下载/评论 | 沿用展品现有规则（提交要求已发布展品，因此无需新增规则） | 同左 | 同左 | 同左 |

## 6. 计分与结果发布规则

1. 资格判定：报名状态为 `REGISTERED`，且竞赛的每个必做任务都存在该用户 `is_current=true` 的提交。不满足者不进入正式排名、不写结果行。
2. 单任务得分：`round(原始得分 / competition_max_score * competition_weight, 4)`，`Decimal` 计算、`ROUND_HALF_UP`。
3. 未提交的选做任务按 0 分计；必做任务缺失按第 1 条整体取消资格。
4. 总分：各任务得分之和再 `round(..., 4)`。
5. 名次：按总分降序采用标准竞赛排名，同分同名次（1、1、3）；不使用提交时间打破并列。
6. 奖项：发布请求可携带可选的 `{registration_id, award}` 列表写入快照。
7. 冻结：发布后所有排行展示读快照；后续评审修改不自动反映，纠错走管理员重新发布并记录日志。
8. 每名参赛人每个任务只取 `is_current=true` 的最终有效提交计分，历史轮次不重复计分。

## 7. API 契约

沿用现有约定：路径前缀 `/api/v1`，字段 snake_case，UTC ISO 时间带 `Z` 后缀，错误体 `{code, message, details?}`，写操作带 `X-CSRF-Token`。下列新增接口的请求/响应模型在批次实现时按现有 schema 风格细化，但路径、执行人与错误码以本节为准。

### 7.1 任务域（批次8）

```text
POST   /api/v1/tasks/{task_id}/participants                领取任务（本人）→ 201
DELETE /api/v1/tasks/{task_id}/participants/me             退出参与（本人）→ 200
GET    /api/v1/tasks/{task_id}/participants                参与人列表 → 200
GET    /api/v1/tasks/{task_id}/submissions                 提交列表（按第5节可见性过滤）→ 200
POST   /api/v1/tasks/{task_id}/submissions                 提交成果 {artifact_id, note?} → 201
POST   /api/v1/task-submissions/{submission_id}/request-revision  退回 {note} → 200
POST   /api/v1/task-submissions/{submission_id}/accept           验收 {note?} → 200
POST   /api/v1/task-submissions/{submission_id}/reject           拒绝 {note} → 200
```

现有接口的行为变更：

- `GET /api/v1/tasks`：新增查询参数 `participated=true`（我参与的）与 `pending_review=true`（待我验收的，即我为发布人且存在待处理提交）；列表项新增 `competition_id`、`competition_title`（随批次9的 `tasks` 竞赛列一并交付）。
- `GET /api/v1/tasks/{task_id}`：响应新增竞赛摘要（属竞赛任务时）、`my_participation`、参与数与提交统计。
- `POST /api/v1/tasks/{task_id}/complete`：按 4.1 节新规则（要求存在已验收提交，允许从 `IN_PROGRESS`/`REVIEWING` 完成）。
- `POST /api/v1/tasks/{task_id}/close`：允许从非终态关闭；新增管理员重开动作 `POST /api/v1/admin/tasks/{task_id}/reopen`。
- `POST /api/v1/tasks`（创建）：不接收 `competition_id`，竞赛任务只能经 7.2 创建。
- 批次8实施时补充的端点与规则修订：
  - `GET /api/v1/artifacts/{artifact_id}/task-submissions`：展品的任务提交来源列表，本人见全部轮次，他人只见 `ACCEPTED` 轮次；响应复用提交模型并附任务摘要（`task.id/title/status`）。
  - 编辑任务的前置状态从仅 `OPEN` 放宽为非终态（`OPEN`/`IN_PROGRESS`/`REVIEWING`）。
  - 关闭任务的前置状态收窄为非终态，`COMPLETED` 任务不可再关闭。
  - `complete` 先校验"存在已验收提交"再校验任务状态：无验收成果时即使状态不符也返回 `TASK_NO_ACCEPTED_RESULT`。

### 7.2 竞赛域（批次9）

```text
POST   /api/v1/competitions/{competition_id}/registrations           报名 → 201
DELETE /api/v1/competitions/{competition_id}/registrations/me        取消报名 → 200
GET    /api/v1/competitions/{competition_id}/tasks                   竞赛任务清单（含我的提交摘要）→ 200
GET    /api/v1/competitions/{competition_id}/results                 排行榜（未发布 → 404）→ 200
POST   /api/v1/admin/competitions/{competition_id}/tasks             创建竞赛任务 → 201
PATCH  /api/v1/admin/competitions/{competition_id}/tasks/{task_id}   修改任务配置 → 200
DELETE /api/v1/admin/competitions/{competition_id}/tasks/{task_id}   删除任务（仅 DRAFT）→ 204
POST   /api/v1/admin/competitions/{competition_id}/publish           发布竞赛 → 200
POST   /api/v1/task-submissions/{submission_id}/competition-review   评审 {raw_score, comment?} → 200
POST   /api/v1/admin/competitions/{competition_id}/publish-results   发布结果 {awards?} → 200
POST   /api/v1/admin/competitions/{competition_id}/archive           归档 → 200
```

现有接口的行为变更：

- `GET /api/v1/competitions`：列表项新增 `status`；员工请求不返回 `DRAFT`。
- `GET /api/v1/competitions/{competition_id}`：响应新增 `status`、`task_count`、`registration_count`、`my_registration`；结果已发布时内嵌排行摘要或链接。
- `POST /api/v1/admin/competitions`：新竞赛初始 `DRAFT`。
- `PATCH /api/v1/admin/competitions/{id}`、`DELETE /api/v1/admin/competitions/{id}`：按 4.5 节新规则。

### 7.3 新增错误码汇总

| 错误码 | HTTP | 场景 |
|---|---|---|
| `TASK_ALREADY_PARTICIPATED` | 409 | 重复领取任务 |
| `TASK_SUBMISSION_EXISTS` | 409 | 已有提交后退出参与 |
| `TASK_NO_ACCEPTED_RESULT` | 409 | 无已验收提交时完成任务 |
| `PARTICIPANT_NOT_FOUND` | 404 | 参与记录不存在 |
| `SUBMISSION_NOT_FOUND` | 404 | 提交不存在 |
| `SUBMISSION_STATE_CONFLICT` | 409 | 提交状态不允许该动作（含对竞赛提交执行验收类动作、对历史轮次操作） |
| `SUBMISSION_ALREADY_PENDING` | 409 | 普通任务已有待处理/已验收的当前提交 |
| `ARTIFACT_NOT_SUBMITTABLE` | 422 | 展品非本人所有或未发布 |
| `COMPETITION_STATE_CONFLICT` | 409 | 竞赛运营状态不允许该动作 |
| `COMPETITION_NO_TASKS` | 409 | 未配置任务即发布 |
| `COMPETITION_CONFIG_LOCKED` | 409 | 报名/提交产生后修改计分配置或删任务 |
| `COMPETITION_REGISTRATION_REQUIRED` | 403 | 未报名参与竞赛任务 |
| `COMPETITION_ALREADY_REGISTERED` | 409 | 重复报名 |
| `COMPETITION_REGISTRATION_CLOSED` | 409 | 报名窗口外 |
| `COMPETITION_SUBMISSION_CLOSED` | 409 | 竞赛任务未开始或已截止 |
| `COMPETITION_SUBMISSION_EXISTS` | 409 | 已有提交后取消报名 |
| `COMPETITION_REVIEWS_INCOMPLETE` | 409 | 评审不全即发布结果 |
| `COMPETITION_NOT_ENDED` | 409 | 竞赛未结束即发布结果 |
| `COMPETITION_HAS_REFERENCES` | 409 | 有关联数据时删除竞赛 |
| `COMPETITION_RESULTS_NOT_PUBLISHED` | 404 | 结果未发布时访问排行榜 |
| `REVIEW_SCORE_INVALID` | 422 | 评审分数越界 |

现有错误码（`TASK_NOT_FOUND`、`COMPETITION_NOT_FOUND`、`TASK_STATE_CONFLICT`、`FORBIDDEN` 等）沿用。

### 7.4 管理动作日志

沿用 `log_admin_action`，新增动作名：`task.reopen`、`competition.publish`、`competition.task_create`、`competition.task_update`、`competition.task_delete`、`competition_review.create`、`competition_review.update`、`competition.publish_results`、`competition.republish_results`、`competition.archive`。管理员代替发布人执行验收类动作用既有动作名（`task.accept` 等按实现命名）并带 `actor_id`。

## 8. 迁移与兼容

拆分为两个迁移，各自随所属批次交付：

- `20260819_0006_closed_loop_tasks`（批次8）：`tasks.status` CHECK 扩展（batch 模式重建表）；新建 `task_participants`、`task_submissions`。
- `20260819_0007_closed_loop_competitions`（批次9）：`tasks` 增加竞赛五列与约束索引（batch 模式重建表）；`competitions.status`；新建 `competition_registrations`、`competition_reviews`、`competition_results`。

兼容规则：

- 从 `20260819_0005` 起连续升级，不修改历史迁移；两个迁移均提供完整 downgrade。
- 存量数据默认值：任务 `competition_id` 为空（保持普通独立任务语义，`competition_*` 列全空）；存量竞赛 `status='PUBLISHED'`；五张新表为空，不伪造参与、提交、报名、评审或结果记录。
- 存量任务状态 `OPEN`/`COMPLETED`/`CLOSED` 在新 CHECK 约束下仍合法，不需迁移值。
- 升级后重跑备份恢复演练，并验证 SQLite 外键完整性（`PRAGMA foreign_key_check`）。
- `backend_c#/MkAIHub.Api/Data/Migrator.cs` 本阶段不同步：C# 版停留在 `20260819_0005`，不能读写已升级到 `0006+` 的数据库，这是第二阶段暂缓同步的预期结果，待闭环稳定后另行评估。

## 9. 测试清单（先写测试，再实现）

### 9.1 后端

迁移：

- 空库从零升级到 `0007`；从 `0005` 带数据升级到 `0006` 再到 `0007`；逐级降级回 `0005`。
- 升级后存量任务为独立任务、存量竞赛 `PUBLISHED`；`PRAGMA foreign_key_check` 为空。
- RESTRICT 生效验证：带参与/提交/报名/结果记录时删除被引用行失败。

状态机（4.1–4.5 节每个动作）：

- 每个动作的成功路径与表中列出的每条失败条件各一条用例（含错误码断言）。
- `REVIEWING` 重算：最后一条待处理提交被处理后任务回到 `IN_PROGRESS`；他人仍有待处理提交时保持 `REVIEWING`。
- 竞赛任务调用 `complete` 被拒；截止时间后提交被拒；窗口边界（`start_at` 前、`end_at` 当刻）行为。

权限：

- 每个新接口按「匿名、无关员工、参与人/报名人、发布人、其他发布人、管理员」矩阵各验证。
- 员工看不到 `DRAFT` 竞赛；结果发布前参赛人取不到评审分数（列表与详情两条路径）。
- 越权直接调用接口返回统一错误体。

约束与计分：

- 重复参与、重复报名、重复轮次的唯一约束。
- 加权计算与舍入（含进位边界）、并列名次、必做缺失不排名、选做 0 分、仅当前提交计分。
- 发布后修改评审不影响快照；重新发布整体替换并保持约束唯一。

兼容回归：

- 现有认证、展品、任务、竞赛、Issues、探索接口在迁移后行为不变（文档化变更除外）。

### 9.2 前端

- 新增 API 封装的参数与错误码到文案的映射。
- 任务/竞赛/提交状态到中文文案与颜色映射。
- 按角色与状态矩阵验证按钮可见性（领取、提交、退回、验收、报名、评审、发布结果）。
- 提交、退回、验收、竞赛任务配置、评审、发布结果表单：提交中禁用（`try/finally` 恢复）、失败保留输入并展示后端错误。

### 9.3 浏览器（批次11执行，此处仅登记范围）

- 管理员 + 两名员工覆盖实施方案第 12 节任务闭环与竞赛闭环场景；刷新后状态保持；越权路由与直接接口调用被拒。

## 10. 批次拆分对照

| 契约项 | 批次8（任务与展品闭环） | 批次9（竞赛、任务与展品闭环） |
|---|---|---|
| 迁移 | `0006`：任务状态 CHECK、`task_participants`、`task_submissions` | `0007`：`tasks` 竞赛列、`competitions.status`、三张竞赛表 |
| 后端 | 领取/退出/提交/退回/验收/拒绝/完成/关闭/重开；`participated`、`pending_review` 筛选；提交表结构预留竞赛多轮语义 | 报名/取消；竞赛任务配置；竞赛提交与评审；计分与结果发布；归档；排行榜 |
| 前端 | 任务详情参与区/提交区/验收区/时间线；展品来源展示；筛选入口 | 竞赛详情任务清单与排行榜；管理页任务配置、评审、结果发布、归档；展品竞赛来源 |
| 不做 | 竞赛评分、报名、结果（提交表仅预留） | — |
