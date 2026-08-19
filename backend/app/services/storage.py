"""Small local-file storage helper used by artifact attachments."""

from __future__ import annotations

import hashlib
import os
import tempfile
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

from fastapi import UploadFile

from app.core.config import Settings
from app.core.errors import AppError


CHUNK_SIZE = 1024 * 1024


@dataclass(frozen=True, slots=True)
class SavedUpload:
    original_name: str
    stored_name: str
    relative_path: str
    extension: str
    mime_type: str
    size_bytes: int
    sha256: str


def upload_root(settings: Settings) -> Path:
    return settings.upload_dir.expanduser().resolve()


def _display_filename(filename: str | None) -> str:
    cleaned = (filename or "").replace("\\", "/").split("/")[-1].strip()
    if not cleaned or any(ord(character) < 32 for character in cleaned) or len(cleaned) > 255:
        raise AppError("FILE_NAME_INVALID", "File name is invalid", status_code=400)
    return cleaned


def save_upload(upload: UploadFile, settings: Settings) -> SavedUpload:
    original_name = _display_filename(upload.filename)
    extension = Path(original_name).suffix.lower().lstrip(".")
    if not extension or extension not in settings.allowed_extensions:
        raise AppError("FILE_TYPE_NOT_ALLOWED", "This file type is not allowed", status_code=415)

    now = datetime.now(UTC)
    stored_name = f"{uuid4().hex}.{extension}"
    relative_path = Path(f"{now.year:04d}") / f"{now.month:02d}" / stored_name
    root = upload_root(settings)
    destination = (root / relative_path).resolve()
    if not destination.is_relative_to(root):
        raise AppError("FILE_PATH_INVALID", "File storage path is invalid", status_code=500)
    destination.parent.mkdir(parents=True, exist_ok=True)
    temp_dir = root / ".tmp"
    temp_dir.mkdir(parents=True, exist_ok=True)

    max_bytes = settings.max_upload_size_mb * 1024 * 1024
    size_bytes = 0
    digest = hashlib.sha256()
    temp_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(dir=temp_dir, delete=False) as temporary:
            temp_path = Path(temporary.name)
            while chunk := upload.file.read(CHUNK_SIZE):
                size_bytes += len(chunk)
                if size_bytes > max_bytes:
                    raise AppError(
                        "FILE_TOO_LARGE",
                        f"File exceeds the {settings.max_upload_size_mb} MB limit",
                        status_code=413,
                    )
                digest.update(chunk)
                temporary.write(chunk)
        if size_bytes == 0:
            raise AppError("FILE_EMPTY", "Empty files cannot be uploaded", status_code=400)
        os.replace(temp_path, destination)
        temp_path = None
    finally:
        if temp_path is not None:
            temp_path.unlink(missing_ok=True)
        upload.file.close()

    return SavedUpload(
        original_name=original_name,
        stored_name=stored_name,
        relative_path=relative_path.as_posix(),
        extension=extension,
        mime_type=(upload.content_type or "application/octet-stream")[:150],
        size_bytes=size_bytes,
        sha256=digest.hexdigest(),
    )


def stored_path(relative_path: str, settings: Settings) -> Path:
    root = upload_root(settings)
    path = (root / relative_path).resolve()
    if not path.is_relative_to(root):
        raise AppError("FILE_PATH_INVALID", "File storage path is invalid", status_code=500)
    return path
