"""Versioned API router for the first authenticated batch."""

from fastapi import APIRouter

from app.api.v1.admin import router as admin_router
from app.api.v1.artifacts import comment_router, router as artifacts_router
from app.api.v1.auth import router as auth_router
from app.api.v1.competitions import admin_router as admin_competitions_router, router as competitions_router
from app.api.v1.explore import router as explore_router
from app.api.v1.files import router as files_router
from app.api.v1.issues import router as issues_router
from app.api.v1.task_submissions import router as task_submissions_router
from app.api.v1.tasks import router as tasks_router


router = APIRouter(prefix="/v1")
router.include_router(auth_router)
router.include_router(admin_router)
router.include_router(admin_competitions_router)
router.include_router(artifacts_router)
router.include_router(comment_router)
router.include_router(files_router)
router.include_router(explore_router)
router.include_router(tasks_router)
router.include_router(task_submissions_router)
router.include_router(issues_router)
router.include_router(competitions_router)

__all__ = ["router"]
