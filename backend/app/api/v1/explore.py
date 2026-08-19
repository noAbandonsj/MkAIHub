"""Minimal explore aggregation endpoint."""

from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy import select
from sqlalchemy.orm import Session

from app.api.v1.deps import get_current_user
from app.db.session import get_db
from app.models import Artifact, User
from app.schemas.artifact import ExploreResponse
from app.services.artifacts import artifact_list_item, artifact_load_options


router = APIRouter(prefix="/explore", tags=["explore"])


@router.get("", response_model=ExploreResponse, summary="Get explore page data")
def explore(
    _user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> ExploreResponse:
    artifacts = list(
        db.scalars(
            select(Artifact)
            .options(*artifact_load_options())
            .where(Artifact.status == "PUBLISHED")
            .order_by(Artifact.published_at.desc(), Artifact.id.desc())
            .limit(6)
        ).all()
    )
    return ExploreResponse(latest_artifacts=[artifact_list_item(item) for item in artifacts])
