from __future__ import annotations

import pytest
from pydantic import ValidationError

from app.core.config import EXAMPLE_SECRET_VALUE, Settings


@pytest.mark.parametrize("secret", [None, "too-short", EXAMPLE_SECRET_VALUE])
def test_production_rejects_missing_weak_or_example_secret(secret: str | None) -> None:
    with pytest.raises(ValidationError, match="APP_SECRET_KEY must be replaced"):
        Settings(_env_file=None, app_env="production", app_secret_key=secret)


def test_production_accepts_a_replaced_secret() -> None:
    settings = Settings(_env_file=None, app_env="production", app_secret_key="x" * 32)

    assert settings.app_env == "production"
