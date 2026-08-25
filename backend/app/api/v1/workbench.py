"""Personal workbench and closure statistics endpoint."""

from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.api.v1.deps import get_current_user
from app.db.session import get_db
from app.models import User
from app.schemas.workbench import WorkbenchResponse
from app.services.workbench import workbench_response


router = APIRouter(prefix="/workbench", tags=["workbench"])


@router.get("", response_model=WorkbenchResponse, summary="Get my workbench summary")
def get_workbench(
    user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> WorkbenchResponse:
    return workbench_response(db, user)
