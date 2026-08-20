"""Operational command-line helpers for MkAIHub."""

from __future__ import annotations

import argparse
import getpass
import shutil
import sqlite3
import sys
import zipfile
from datetime import datetime
from pathlib import Path
from typing import Sequence

from sqlalchemy import select
from sqlalchemy.engine import make_url
from sqlalchemy.exc import IntegrityError

from app.core.config import Settings, get_settings
from app.db.session import create_engine_from_url, session_factory
from app.models import User
from app.schemas.auth import AdminUserCreate, UserRole
from app.services.security import hash_password


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m app.cli")
    commands = parser.add_subparsers(dest="command", required=True)
    create_admin = commands.add_parser("create-admin", help="Create the first system administrator")
    create_admin.add_argument("--username")
    create_admin.add_argument("--display-name")
    create_admin.add_argument("--password", help="Avoid when possible; an interactive prompt is safer")
    create_admin.add_argument("--database-url")
    backup = commands.add_parser("backup", help="Back up the SQLite database and uploaded files")
    backup.add_argument("--database-url")
    backup.add_argument("--output-dir", help="Directory for backup artifacts (default: <data_dir>/backups)")
    restore = commands.add_parser("restore", help="Restore the database and uploads from a backup")
    restore.add_argument("--database-url")
    restore.add_argument("--db-file", required=True, help="Backup database file created by `backup`")
    restore.add_argument("--uploads-zip", help="Backup uploads archive created by `backup`")
    restore.add_argument(
        "--yes",
        action="store_true",
        help="Confirm that the current database and uploads may be overwritten",
    )
    return parser


def _resolve_settings(args: argparse.Namespace) -> Settings:
    base_settings = get_settings()
    if args.database_url:
        # Keep every other configured path (data/upload dirs) from the base
        # settings and only swap the database URL.
        return base_settings.model_copy(update={"database_url": args.database_url})
    return base_settings


def create_admin(args: argparse.Namespace) -> int:
    """Create a system administrator using arguments or safe prompts."""

    settings = _resolve_settings(args)
    username = args.username or input("Username: ")
    display_name = args.display_name or input("Display name: ")
    password = args.password
    if password is None:
        password = getpass.getpass("Password: ")
        confirmation = getpass.getpass("Confirm password: ")
        if password != confirmation:
            print("Passwords do not match", file=sys.stderr)
            return 2

    try:
        payload = AdminUserCreate(
            username=username,
            display_name=display_name,
            password=password,
            role=UserRole.SYSTEM_ADMIN,
        )
    except ValueError as exc:
        print(f"Invalid administrator details: {exc}", file=sys.stderr)
        return 2

    engine = create_engine_from_url(settings.database_url)
    factory = session_factory(engine)
    try:
        with factory() as db:
            if db.scalar(select(User.id).where(User.username == payload.username)) is not None:
                print("Username is already in use", file=sys.stderr)
                return 2
            db.add(
                User(
                    username=payload.username,
                    display_name=payload.display_name,
                    password_hash=hash_password(payload.password),
                    role=UserRole.SYSTEM_ADMIN.value,
                    is_active=True,
                )
            )
            try:
                db.commit()
            except IntegrityError:
                db.rollback()
                print("Username is already in use", file=sys.stderr)
                return 2
    finally:
        engine.dispose()

    print(f"Created system administrator: {payload.username}")
    return 0


def _database_path(settings: Settings) -> Path:
    url = make_url(settings.database_url)
    path = url.database
    if not path or path == ":memory:":
        print("Only file-backed SQLite databases can be backed up", file=sys.stderr)
        raise SystemExit(2)
    return Path(path)


def run_backup(args: argparse.Namespace) -> int:
    """Create a consistent SQLite snapshot plus a zip of uploaded files."""

    settings = _resolve_settings(args)
    db_path = _database_path(settings)
    if not db_path.is_file():
        print(f"Database file not found: {db_path}", file=sys.stderr)
        return 2

    output_dir = Path(args.output_dir) if args.output_dir else settings.data_dir / "backups"
    output_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")

    db_target = output_dir / f"mkaihub-backup-{stamp}.db"
    source = sqlite3.connect(db_path)
    try:
        destination = sqlite3.connect(db_target)
        try:
            source.backup(destination)
        finally:
            destination.close()
    finally:
        source.close()

    uploads_target = output_dir / f"mkaihub-backup-{stamp}-uploads.zip"
    file_count = 0
    if settings.upload_dir.is_dir():
        with zipfile.ZipFile(uploads_target, "w", zipfile.ZIP_DEFLATED) as archive:
            for file in sorted(settings.upload_dir.rglob("*")):
                if file.is_file():
                    archive.write(file, file.relative_to(settings.upload_dir).as_posix())
                    file_count += 1

    print(f"Database backup: {db_target}")
    print(f"Uploads backup: {uploads_target} ({file_count} files)")
    return 0


def run_restore(args: argparse.Namespace) -> int:
    """Overwrite the current database (and uploads) from a backup.

    The application must be stopped before restoring; active connections would
    keep writing to the replaced database via the old WAL files.
    """

    settings = _resolve_settings(args)
    db_file = Path(args.db_file)
    if not db_file.is_file():
        print(f"Backup database not found: {db_file}", file=sys.stderr)
        return 2

    check = sqlite3.connect(db_file)
    try:
        integrity = check.execute("PRAGMA integrity_check").fetchone()
    finally:
        check.close()
    if not integrity or integrity[0] != "ok":
        print("Backup database failed the integrity check; refusing to restore", file=sys.stderr)
        return 2

    if not args.yes:
        print("Restore overwrites the current database and uploads. Re-run with --yes to confirm.", file=sys.stderr)
        return 2

    db_path = _database_path(settings)
    for suffix in ("-wal", "-shm"):
        Path(f"{db_path}{suffix}").unlink(missing_ok=True)
    db_path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(db_file, db_path)

    restored_files = 0
    if args.uploads_zip:
        uploads_zip = Path(args.uploads_zip)
        if not uploads_zip.is_file():
            print(f"Uploads archive not found: {uploads_zip}", file=sys.stderr)
            return 2
        settings.upload_dir.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(uploads_zip) as archive:
            restored_files = len(archive.namelist())
            archive.extractall(settings.upload_dir)

    print(f"Database restored from: {db_file}")
    if args.uploads_zip:
        print(f"Uploads restored from: {args.uploads_zip} ({restored_files} files)")
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    if args.command == "create-admin":
        return create_admin(args)
    if args.command == "backup":
        return run_backup(args)
    if args.command == "restore":
        return run_restore(args)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())

