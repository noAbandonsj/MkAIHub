"""Password and session-token cryptographic helpers."""

from __future__ import annotations

import hashlib

from argon2 import PasswordHasher
from argon2.exceptions import InvalidHashError, VerificationError, VerifyMismatchError
from argon2.low_level import Type


# The parameters are intentionally explicit so the stored format is Argon2id
# and can be tuned in one place as the deployment hardware changes.
password_hasher = PasswordHasher(
    time_cost=2,
    memory_cost=19_456,
    parallelism=2,
    hash_len=32,
    salt_len=16,
    type=Type.ID,
)

def validate_password(password: str) -> str:
    """Validate and return a password without modifying its contents."""

    if not isinstance(password, str) or not 8 <= len(password) <= 128:
        raise ValueError("Password must be between 8 and 128 characters")
    return password


def hash_password(password: str) -> str:
    """Hash a password with Argon2id."""

    return password_hasher.hash(validate_password(password))


def verify_password(password: str, password_hash: str) -> bool:
    """Verify a password, returning false for all malformed/mismatched hashes."""

    try:
        return password_hasher.verify(password_hash, password)
    except (VerifyMismatchError, VerificationError, InvalidHashError):
        return False


def hash_session_token(token: str) -> str:
    """Hash a raw session cookie token before it reaches the database."""

    return hashlib.sha256(token.encode("utf-8")).hexdigest()
