"""Operational command-line helpers for MkAIHub."""

from __future__ import annotations

import argparse
import getpass
import sys
from typing import Sequence

from sqlalchemy import select
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
    return parser


def create_admin(args: argparse.Namespace) -> int:
    """Create a system administrator using arguments or safe prompts."""

    base_settings = get_settings()
    settings = (
        Settings(database_url=args.database_url, _env_file=None)
        if args.database_url
        else base_settings
    )
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


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    if args.command == "create-admin":
        return create_admin(args)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())

