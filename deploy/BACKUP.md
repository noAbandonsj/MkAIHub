# MkAIHub 备份与恢复说明

MkAIHub 的全部业务数据由两部分组成，备份与恢复必须同时覆盖：

1. SQLite 数据库文件（默认 `data/mkaihub.db`，由 `DATABASE_URL` 指定）。
2. 上传文件目录（默认 `storage/uploads/`，由 `UPLOAD_DIR` 指定）。

两个目录均已通过 Docker Compose 卷挂载到宿主机；上传文件不进入数据库，因此只备份数据库是不完整的。

## 执行备份

`backup` 子命令使用 SQLite 在线备份 API 生成一致性快照（包含已提交的 WAL 内容），并把上传目录打成 zip。产物默认写入 `<DATA_DIR>/backups/`：

```bash
# 宿主机（在 backend/ 目录，读取仓库根 .env 配置）
uv run python -m app.cli backup

# 容器内（产物落在宿主机 data/backups/ 卷目录）
docker compose --env-file .env -f deploy/docker-compose.yml exec app python -m app.cli backup
```

每次备份生成两个同名时间戳文件：

```text
mkaihub-backup-20260820-112926.db             # 数据库一致性快照
mkaihub-backup-20260820-112926-uploads.zip    # 上传目录完整归档
```

可选参数：`--output-dir` 指定产物目录；`--database-url` 临时指定其他数据库（其余路径配置仍取 `.env`）。

建议使用 cron（Linux）每日执行，例如凌晨 3 点：

```cron
0 3 * * * cd /opt/mkaihub/backend && /usr/local/bin/uv run python -m app.cli backup >> /var/log/mkaihub-backup.log 2>&1
```

备份产物应按保留策略转存到独立磁盘或对象存储；本目录备份不提供加密与压缩轮转。

## 执行恢复

恢复会**覆盖**当前数据库和上传目录中的同名文件，必须先停止应用容器（活动连接会通过旧 WAL 继续写入被替换的库文件）：

```bash
docker compose --env-file .env -f deploy/docker-compose.yml down

# 找到要恢复的备份（宿主机 data/backups/）
ls data/backups/

# 恢复数据库与上传文件（--yes 确认覆盖）
cd backend
uv run python -m app.cli restore \
  --db-file ../data/backups/mkaihub-backup-20260820-112926.db \
  --uploads-zip ../data/backups/mkaihub-backup-20260820-112926-uploads.zip \
  --yes

docker compose --env-file .env -f deploy/docker-compose.yml up -d
```

恢复行为说明：

- 恢复前会执行 `PRAGMA integrity_check`，校验不通过则拒绝恢复。
- 恢复会删除目标库残留的 `-wal` / `-shm` 文件后整体替换数据库文件。
- 上传目录按 zip 内容解压覆盖；**不会删除**备份之后新增的上传文件。如需完全回到备份点，先清空上传目录再执行恢复。
- 只恢复数据库（不动上传文件）时省略 `--uploads-zip` 即可。

## 恢复演练记录

- 2026-08-20：在本地环境完成一次完整演练——执行 `backup` 后新增管理员账号并改动上传目录，随后 `restore --yes`；恢复后新增账号消失，被删除的上传文件与内容还原，数据库与文件均回到备份点。
