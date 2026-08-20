"""Comment serialization shared by artifact and issue routes."""

from __future__ import annotations

from app.models import Comment
from app.schemas.artifact import CommentRead, UserSummary


def comment_read(comment: Comment) -> CommentRead:
    return CommentRead(
        id=comment.id,
        artifact_id=comment.artifact_id,
        issue_id=comment.issue_id,
        author=UserSummary.model_validate(comment.author),
        content=comment.content,
        status=comment.status,
        created_at=comment.created_at,
        updated_at=comment.updated_at,
    )
