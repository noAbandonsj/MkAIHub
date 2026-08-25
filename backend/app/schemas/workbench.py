"""Personal workbench counts and recomputable closure statistics."""

from __future__ import annotations

from pydantic import BaseModel


class WorkbenchCounts(BaseModel):
    participated_tasks: int
    competition_tasks: int
    pending_task_reviews: int
    pending_competition_reviews: int


class ClosureStatistics(BaseModel):
    participations: int
    submissions: int
    accepted_submissions: int
    competition_task_completions: int
    published_results: int


class WorkbenchResponse(BaseModel):
    counts: WorkbenchCounts
    statistics: ClosureStatistics
