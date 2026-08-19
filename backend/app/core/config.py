"""Environment-backed application settings."""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path

from pydantic import SecretStr, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
REPOSITORY_ENV_FILE = REPOSITORY_ROOT / ".env"
EXAMPLE_SECRET_VALUE = "replace-with-a-random-secret-of-at-least-32-characters"


class Settings(BaseSettings):
    """Runtime configuration loaded from environment variables and ``.env``."""

    model_config = SettingsConfigDict(
        env_file=REPOSITORY_ENV_FILE,
        env_file_encoding="utf-8",
        env_prefix="",
        case_sensitive=False,
        extra="ignore",
    )

    app_env: str = "development"
    app_name: str = "MkAIHub"
    app_secret_key: SecretStr | None = None

    database_url: str = "sqlite:///./data/mkaihub.db"
    data_dir: Path = Path("./data")
    upload_dir: Path = Path("./storage/uploads")
    max_upload_size_mb: int = 50
    max_attachments_per_artifact: int = 10
    allowed_upload_extensions: str = (
        "pdf,docx,xlsx,pptx,md,txt,csv,json,png,jpg,jpeg,webp,"
        "py,js,ts,vue,sql,yaml,yml,toml,ipynb,zip"
    )

    session_cookie_name: str = "mkaihub_session"
    session_ttl_hours: int = 24
    frontend_origin: str | None = None

    log_level: str = "INFO"
    initial_admin_username: str | None = None

    @model_validator(mode="after")
    def validate_production_secret(self) -> Settings:
        """Require a secret in production without embedding one in source."""

        if self.app_env.lower() == "production":
            secret_value = self.app_secret_key.get_secret_value() if self.app_secret_key else ""
            if len(secret_value) < 32 or secret_value == EXAMPLE_SECRET_VALUE:
                raise ValueError(
                    "APP_SECRET_KEY must be replaced with a random value of at least 32 characters "
                    "in production"
                )
        if self.max_upload_size_mb <= 0:
            raise ValueError("MAX_UPLOAD_SIZE_MB must be greater than zero")
        if self.max_attachments_per_artifact <= 0:
            raise ValueError("MAX_ATTACHMENTS_PER_ARTIFACT must be greater than zero")
        if self.session_ttl_hours <= 0:
            raise ValueError("SESSION_TTL_HOURS must be greater than zero")
        return self

    @property
    def allowed_extensions(self) -> frozenset[str]:
        """Return normalized extensions for future upload validation."""

        return frozenset(
            extension.strip().lower().lstrip(".")
            for extension in self.allowed_upload_extensions.split(",")
            if extension.strip()
        )

    @property
    def frontend_origins(self) -> list[str]:
        """Return configured CORS origins, ignoring empty values."""

        if not self.frontend_origin:
            return []
        return [origin.strip() for origin in self.frontend_origin.split(",") if origin.strip()]

    @property
    def project_root(self) -> Path:
        """Return the repository root containing ``backend`` and ``frontend``."""

        return Path(__file__).resolve().parents[3]

    @property
    def backend_root(self) -> Path:
        """Return the backend source root."""

        return Path(__file__).resolve().parents[2]

    @property
    def frontend_dist(self) -> Path:
        """Return the optional production SPA directory."""

        return self.project_root / "frontend" / "dist"


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    """Return the process-wide settings instance."""

    return Settings()
