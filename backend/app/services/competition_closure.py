"""Competition registration, task configuration, review, scoring, and results.

All competition-closure state transitions live in this module so the batch-7
contract state machine stays in one place; routes only parse input, audit,
and serialize output.
"""

from __future__ import annotations

from datetime import UTC, datetime
from decimal import ROUND_HALF_UP, Decimal

from sqlalchemy import delete, exists, func, select
from sqlalchemy.orm import Session, joinedload

from app.core.errors import AppError
from app.models import (
    Competition,
    CompetitionRegistration,
    CompetitionResult,
    CompetitionReview,
    Task,
    TaskParticipant,
    TaskSubmission,
    User,
)
from app.schemas.artifact import UserSummary
from app.schemas.auth import UserRole
from app.schemas.competition import (
    CompetitionRegistrationRead,
    CompetitionResultRow,
    CompetitionResultsRead,
    CompetitionTaskRead,
    CompetitionTaskSubmissionSummary,
)
from app.schemas.competition import RegistrationStatus
from app.services.competitions import get_competition
from app.services.task_closure import (
    _require_active_participant,
    load_submittable_artifact,
)

DRAFT = "DRAFT"
PUBLISHED = "PUBLISHED"
RESULT_PUBLISHED = "RESULT_PUBLISHED"
ARCHIVED = "ARCHIVED"

REGISTERED = RegistrationStatus.REGISTERED.value
CANCELLED = RegistrationStatus.CANCELLED.value

SCORE_QUANTUM = Decimal("0.0001")


def _utcnow() -> datetime:
    return datetime.now(UTC)


def _as_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=UTC)
    return value.astimezone(UTC)


def _is_admin(user: User) -> bool:
    return user.role == UserRole.SYSTEM_ADMIN.value


def get_competition_for_viewer(db: Session, competition_id: int, user: User) -> Competition:
    """DRAFT competitions stay invisible to non-administrators (404)."""

    competition = get_competition(db, competition_id)
    if competition.status == DRAFT and not _is_admin(user):
        raise AppError("COMPETITION_NOT_FOUND", "Competition not found", status_code=404)
    return competition


def find_registration(db: Session, competition_id: int, user_id: int) -> CompetitionRegistration | None:
    return db.scalar(
        select(CompetitionRegistration).where(
            CompetitionRegistration.competition_id == competition_id,
            CompetitionRegistration.user_id == user_id,
        )
    )


def _sync_registration_participants(
    db: Session,
    competition_id: int,
    user_id: int,
    *,
    active: bool,
    changed_at: datetime,
) -> None:
    """Keep competition-task participation aligned with one registration."""

    tasks = list(db.scalars(select(Task).where(Task.competition_id == competition_id)).all())
    if not tasks:
        return
    task_ids = [task.id for task in tasks]
    participants = {
        participant.task_id: participant
        for participant in db.scalars(
            select(TaskParticipant).where(
                TaskParticipant.user_id == user_id,
                TaskParticipant.task_id.in_(task_ids),
            )
        ).all()
    }
    for task in tasks:
        participant = participants.get(task.id)
        if active:
            if participant is None:
                db.add(
                    TaskParticipant(
                        task_id=task.id,
                        user_id=user_id,
                        status="ACTIVE",
                        joined_at=changed_at,
                    )
                )
                continue
            participant.status = "ACTIVE"
            participant.joined_at = changed_at
            participant.left_at = None
            participant.updated_at = changed_at
        elif participant is not None:
            participant.status = "LEFT"
            participant.left_at = changed_at
            participant.updated_at = changed_at


def register_competition(db: Session, competition: Competition, user: User) -> CompetitionRegistration:
    if competition.status != PUBLISHED:
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Only a published competition accepts registrations",
            status_code=409,
        )
    now = _utcnow()
    if not _as_utc(competition.start_at) <= now < _as_utc(competition.end_at):
        raise AppError(
            "COMPETITION_REGISTRATION_CLOSED",
            "The registration window is closed",
            status_code=409,
        )
    registration = find_registration(db, competition.id, user.id)
    if registration is None:
        registration = CompetitionRegistration(
            competition_id=competition.id,
            user_id=user.id,
            status=REGISTERED,
            registered_at=now,
        )
        db.add(registration)
    elif registration.status == REGISTERED:
        raise AppError(
            "COMPETITION_ALREADY_REGISTERED",
            "You already registered for this competition",
            status_code=409,
        )
    else:
        registration.status = REGISTERED
        registration.registered_at = now
        registration.cancelled_at = None
    _sync_registration_participants(
        db,
        competition.id,
        user.id,
        active=True,
        changed_at=now,
    )
    db.commit()
    db.refresh(registration)
    return registration


def cancel_registration(db: Session, competition: Competition, user: User) -> CompetitionRegistration:
    registration = find_registration(db, competition.id, user.id)
    if registration is None or registration.status != REGISTERED:
        raise AppError(
            "REGISTRATION_NOT_FOUND",
            "You are not registered for this competition",
            status_code=404,
        )
    if _utcnow() >= _as_utc(competition.end_at):
        raise AppError(
            "COMPETITION_REGISTRATION_CLOSED",
            "The registration window is closed",
            status_code=409,
        )
    if user_has_competition_submissions(db, competition.id, user.id):
        raise AppError(
            "COMPETITION_SUBMISSION_EXISTS",
            "A registrant with submissions cannot cancel the registration",
            status_code=409,
        )
    now = _utcnow()
    registration.status = CANCELLED
    registration.cancelled_at = now
    _sync_registration_participants(
        db,
        competition.id,
        user.id,
        active=False,
        changed_at=now,
    )
    db.commit()
    db.refresh(registration)
    return registration


def competition_task_ids(db: Session, competition_id: int):
    return select(Task.id).where(Task.competition_id == competition_id)


def has_active_registrations(db: Session, competition_id: int) -> bool:
    return bool(
        db.scalar(
            select(
                exists().where(
                    CompetitionRegistration.competition_id == competition_id,
                    CompetitionRegistration.status == REGISTERED,
                )
            )
        )
    )


def has_competition_submissions(db: Session, competition_id: int) -> bool:
    return bool(
        db.scalar(
            select(
                exists().where(
                    TaskSubmission.task_id.in_(competition_task_ids(db, competition_id))
                )
            )
        )
    )


def user_has_competition_submissions(db: Session, competition_id: int, user_id: int) -> bool:
    participant_ids = select(TaskParticipant.id).where(
        TaskParticipant.user_id == user_id,
        TaskParticipant.task_id.in_(competition_task_ids(db, competition_id)),
    )
    return bool(
        db.scalar(select(exists().where(TaskSubmission.participant_id.in_(participant_ids))))
    )


def competition_has_references(db: Session, competition_id: int) -> bool:
    """Any task, registration (even cancelled), or result blocks physical deletion."""

    has_tasks = bool(
        db.scalar(select(exists().where(Task.competition_id == competition_id)))
    )
    has_registrations = bool(
        db.scalar(
            select(exists().where(CompetitionRegistration.competition_id == competition_id))
        )
    )
    has_results = bool(
        db.scalar(select(exists().where(CompetitionResult.competition_id == competition_id)))
    )
    return has_tasks or has_registrations or has_results


def _config_locked(db: Session, competition: Competition) -> bool:
    """Scoring configuration freezes once registrations or submissions exist."""

    return has_active_registrations(db, competition.id) or has_competition_submissions(db, competition.id)


def _validate_task_deadline(competition: Competition, deadline_at: datetime | None) -> None:
    if deadline_at is not None and _as_utc(deadline_at) > _as_utc(competition.end_at):
        raise AppError(
            "COMPETITION_TIME_CONFLICT",
            "A competition task deadline cannot be later than the competition end",
            status_code=422,
        )


def create_competition_task(db: Session, competition: Competition, actor: User, payload) -> Task:
    if competition.status not in (DRAFT, PUBLISHED):
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Tasks can only be configured on draft or published competitions",
            status_code=409,
        )
    if competition.status == PUBLISHED and _config_locked(db, competition):
        raise AppError(
            "COMPETITION_CONFIG_LOCKED",
            "The competition configuration is locked by registrations or submissions",
            status_code=409,
        )
    _validate_task_deadline(competition, payload.deadline_at)
    task = Task(
        title=payload.title,
        description=payload.description,
        deadline_at=payload.deadline_at,
        creator_id=actor.id,
        status="OPEN",
        competition_id=competition.id,
        competition_required=payload.required,
        competition_sort_order=payload.sort_order,
        competition_max_score=payload.max_score,
        competition_weight=payload.weight,
    )
    db.add(task)
    db.commit()
    db.refresh(task)
    return task


def get_competition_task(db: Session, competition: Competition, task_id: int) -> Task:
    task = db.scalar(
        select(Task).where(Task.id == task_id, Task.competition_id == competition.id)
    )
    if task is None:
        raise AppError("TASK_NOT_FOUND", "Task not found", status_code=404)
    return task


CONFIG_FIELDS = ("title", "description", "deadline_at", "required", "max_score", "weight")


def update_competition_task(db: Session, competition: Competition, task: Task, payload) -> Task:
    changes = payload.model_dump(exclude_unset=True)
    full_edit = competition.status == DRAFT or (
        competition.status == PUBLISHED and not _config_locked(db, competition)
    )
    touched_config = [field for field in CONFIG_FIELDS if field in changes]
    if not full_edit and touched_config:
        if competition.status == PUBLISHED:
            raise AppError(
                "COMPETITION_CONFIG_LOCKED",
                "Only the sort order can be changed after registrations or submissions",
                status_code=409,
            )
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Only the sort order can be changed on this competition",
            status_code=409,
        )
    if "deadline_at" in changes:
        _validate_task_deadline(competition, changes["deadline_at"])
    field_map = {
        "title": "title",
        "description": "description",
        "deadline_at": "deadline_at",
        "required": "competition_required",
        "sort_order": "competition_sort_order",
        "max_score": "competition_max_score",
        "weight": "competition_weight",
    }
    for payload_field, column in field_map.items():
        if payload_field in changes:
            setattr(task, column, changes[payload_field])
    if changes:
        task.updated_at = _utcnow()
        db.commit()
    db.refresh(task)
    return task


def delete_competition_task(db: Session, competition: Competition, task: Task) -> None:
    if competition.status != DRAFT:
        if _config_locked(db, competition):
            raise AppError(
                "COMPETITION_CONFIG_LOCKED",
                "Tasks can only be deleted while the competition is a draft",
                status_code=409,
            )
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Tasks can only be deleted while the competition is a draft",
            status_code=409,
        )
    db.delete(task)
    db.commit()


def publish_competition(db: Session, competition: Competition) -> Competition:
    if competition.status != DRAFT:
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Only a draft competition can be published",
            status_code=409,
        )
    task_count = int(
        db.scalar(
            select(func.count()).select_from(Task).where(Task.competition_id == competition.id)
        )
        or 0
    )
    if task_count == 0:
        raise AppError(
            "COMPETITION_NO_TASKS",
            "A competition needs at least one task before publishing",
            status_code=409,
        )
    competition.status = PUBLISHED
    competition.updated_at = _utcnow()
    db.commit()
    db.refresh(competition)
    return competition


def effective_deadline(task: Task, competition: Competition) -> datetime:
    return _as_utc(task.deadline_at) if task.deadline_at is not None else _as_utc(competition.end_at)


def create_competition_submission(
    db: Session,
    task: Task,
    user: User,
    artifact_id: int,
    note: str | None,
) -> TaskSubmission:
    """Submit a round to a competition task: registration and window gated."""

    competition = db.get(Competition, task.competition_id)
    registration = find_registration(db, task.competition_id, user.id)
    if registration is None or registration.status != REGISTERED:
        raise AppError(
            "COMPETITION_REGISTRATION_REQUIRED",
            "A valid registration is required to submit to this competition",
            status_code=403,
        )
    participant = _require_active_participant(db, task, user)
    if task.status != "OPEN":
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "The competition task is disabled",
            status_code=409,
        )
    if competition is None or competition.status != PUBLISHED:
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "The competition does not accept submissions",
            status_code=409,
        )
    now = _utcnow()
    if not _as_utc(competition.start_at) <= now < effective_deadline(task, competition):
        raise AppError(
            "COMPETITION_SUBMISSION_CLOSED",
            "The competition task submission window is closed",
            status_code=409,
        )
    artifact = load_submittable_artifact(db, user, artifact_id)
    current = db.scalar(
        select(TaskSubmission).where(
            TaskSubmission.participant_id == participant.id,
            TaskSubmission.is_current.is_(True),
        )
    )
    if current is not None:
        current.is_current = False
    last_round = int(
        db.scalar(
            select(func.max(TaskSubmission.round_no)).where(
                TaskSubmission.participant_id == participant.id
            )
        )
        or 0
    )
    submission = TaskSubmission(
        task_id=task.id,
        participant_id=participant.id,
        artifact_id=artifact.id,
        round_no=last_round + 1,
        note=note,
        status="SUBMITTED",
        is_current=True,
        submitted_at=now,
    )
    db.add(submission)
    db.commit()
    db.refresh(submission)
    return submission


def review_competition_submission(
    db: Session,
    submission: TaskSubmission,
    reviewer: User,
    raw_score: Decimal,
    comment: str | None,
) -> tuple[CompetitionReview, bool]:
    """Upsert the single official review of a current competition submission."""

    if not _is_admin(reviewer):
        raise AppError("FORBIDDEN", "Only administrators can review competition submissions", status_code=403)
    task = submission.task
    if task.competition_id is None:
        raise AppError(
            "SUBMISSION_STATE_CONFLICT",
            "The submission does not belong to a competition task",
            status_code=409,
        )
    if not submission.is_current:
        raise AppError(
            "SUBMISSION_STATE_CONFLICT",
            "Only the current submission round can be reviewed",
            status_code=409,
        )
    competition = task.competition
    if competition is None or competition.status != PUBLISHED:
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Reviews are only accepted while the competition is published",
            status_code=409,
        )
    max_score = task.competition_max_score or Decimal("0")
    if raw_score < 0 or raw_score > max_score:
        raise AppError(
            "REVIEW_SCORE_INVALID",
            "The review score is outside the task score range",
            status_code=422,
            details={"max_score": f"{max_score:.2f}"},
        )
    review = submission.competition_review
    created = review is None
    now = _utcnow()
    if review is None:
        review = CompetitionReview(
            task_submission_id=submission.id,
            reviewer_id=reviewer.id,
            raw_score=raw_score,
            comment=comment,
            reviewed_at=now,
        )
        db.add(review)
    else:
        review.reviewer_id = reviewer.id
        review.raw_score = raw_score
        review.comment = comment
        review.reviewed_at = now
    db.commit()
    db.refresh(review)
    # The relationship attribute was already loaded (as None) before the
    # insert; refresh it so serialization sees the new review row.
    db.refresh(submission)
    return review, created


class ComputedResult:
    """One ranked participant row derived from current reviews."""

    def __init__(self, registration: CompetitionRegistration, total_score: Decimal, rank: int) -> None:
        self.registration = registration
        self.total_score = total_score
        self.rank = rank


def compute_results(db: Session, competition: Competition) -> tuple[list[ComputedResult], list[dict]]:
    """Weighted totals with standard competition ranking; missing reviews reported."""

    tasks = list(
        db.scalars(
            select(Task)
            .where(Task.competition_id == competition.id)
            .order_by(Task.competition_sort_order.asc(), Task.id.asc())
        ).all()
    )
    registrations = list(
        db.scalars(
            select(CompetitionRegistration)
            .options(joinedload(CompetitionRegistration.user))
            .where(
                CompetitionRegistration.competition_id == competition.id,
                CompetitionRegistration.status == REGISTERED,
            )
            .order_by(CompetitionRegistration.id.asc())
        ).all()
    )
    task_ids = [task.id for task in tasks]
    current_by_user: dict[int, dict[int, TaskSubmission]] = {}
    if task_ids:
        rows = db.execute(
            select(TaskSubmission, TaskParticipant.user_id)
            .join(TaskParticipant, TaskSubmission.participant_id == TaskParticipant.id)
            .where(
                TaskSubmission.task_id.in_(task_ids),
                TaskSubmission.is_current.is_(True),
            )
        ).all()
        for submission, user_id in rows:
            current_by_user.setdefault(user_id, {})[submission.task_id] = submission
    submission_ids = [
        submission.id
        for by_task in current_by_user.values()
        for submission in by_task.values()
    ]
    reviews_by_submission: dict[int, CompetitionReview] = {}
    if submission_ids:
        reviews = db.scalars(
            select(CompetitionReview).where(CompetitionReview.task_submission_id.in_(submission_ids))
        ).all()
        reviews_by_submission = {review.task_submission_id: review for review in reviews}

    missing: list[dict] = []
    qualified: list[tuple[CompetitionRegistration, Decimal]] = []
    for registration in registrations:
        by_task = current_by_user.get(registration.user_id, {})
        required_missing = any(
            task.competition_required and task.id not in by_task for task in tasks
        )
        if required_missing:
            continue
        total = Decimal("0")
        for task in tasks:
            submission = by_task.get(task.id)
            if submission is None:
                continue  # Optional task without a submission scores zero.
            review = reviews_by_submission.get(submission.id)
            if review is None:
                missing.append(
                    {
                        "username": registration.user.username,
                        "task_id": task.id,
                        "task_title": task.title,
                    }
                )
                continue
            task_score = (
                review.raw_score / task.competition_max_score * task.competition_weight
            ).quantize(SCORE_QUANTUM, rounding=ROUND_HALF_UP)
            total += task_score
        qualified.append((registration, total.quantize(SCORE_QUANTUM, rounding=ROUND_HALF_UP)))

    qualified.sort(key=lambda item: (-item[1], item[0].id))
    results: list[ComputedResult] = []
    previous_total: Decimal | None = None
    previous_rank = 0
    for index, (registration, total) in enumerate(qualified, start=1):
        rank = index if previous_total is None or total != previous_total else previous_rank
        results.append(ComputedResult(registration, total, rank))
        previous_total, previous_rank = total, rank
    return results, missing


def publish_results(
    db: Session,
    competition: Competition,
    actor: User,
    awards: list[dict] | None,
) -> tuple[Competition, bool, list[ComputedResult]]:
    """Freeze (or replace) the result snapshot; returns (competition, republished, rows)."""

    if competition.status not in (PUBLISHED, RESULT_PUBLISHED):
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Only a published competition can publish or republish results",
            status_code=409,
        )
    republished = competition.status == RESULT_PUBLISHED
    if not republished and _utcnow() < _as_utc(competition.end_at):
        raise AppError(
            "COMPETITION_NOT_ENDED",
            "Results can only be published after the competition ends",
            status_code=409,
        )
    results, missing = compute_results(db, competition)
    if missing:
        raise AppError(
            "COMPETITION_REVIEWS_INCOMPLETE",
            "Current submissions without a review block the result publication",
            status_code=409,
            details={"missing": missing},
        )
    award_by_registration: dict[int, str] = {}
    if awards:
        valid_ids = {row.registration.id for row in results}
        for award_input in awards:
            registration_id = award_input["registration_id"]
            if registration_id not in valid_ids or registration_id in award_by_registration:
                raise AppError(
                    "COMPETITION_AWARD_INVALID",
                    "Awards must map to ranked registrations exactly once",
                    status_code=422,
                )
            award_by_registration[registration_id] = award_input["award"]
    now = _utcnow()
    db.execute(delete(CompetitionResult).where(CompetitionResult.competition_id == competition.id))
    for row in results:
        db.add(
            CompetitionResult(
                competition_id=competition.id,
                registration_id=row.registration.id,
                total_score=row.total_score,
                rank=row.rank,
                award=award_by_registration.get(row.registration.id),
                published_by=actor.id,
                published_at=now,
            )
        )
    competition.status = RESULT_PUBLISHED
    competition.updated_at = now
    db.commit()
    db.refresh(competition)
    return competition, republished, results


def archive_competition(db: Session, competition: Competition) -> Competition:
    can_archive = competition.status == RESULT_PUBLISHED or (
        competition.status == PUBLISHED and not has_active_registrations(db, competition.id)
    )
    if not can_archive:
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "Only a result-published competition, or one without registrations, can be archived",
            status_code=409,
        )
    competition.status = ARCHIVED
    competition.updated_at = _utcnow()
    db.commit()
    db.refresh(competition)
    return competition


def registration_read(registration: CompetitionRegistration) -> CompetitionRegistrationRead:
    return CompetitionRegistrationRead(
        id=registration.id,
        competition_id=registration.competition_id,
        user=UserSummary.model_validate(registration.user),
        status=registration.status,
        registered_at=registration.registered_at,
        cancelled_at=registration.cancelled_at,
    )


def competition_tasks_read(db: Session, competition: Competition, viewer: User) -> list[CompetitionTaskRead]:
    tasks = list(
        db.scalars(
            select(Task)
            .options(joinedload(Task.creator))
            .where(Task.competition_id == competition.id)
            .order_by(Task.competition_sort_order.asc(), Task.id.asc())
        ).all()
    )
    task_ids = [task.id for task in tasks]
    my_current: dict[int, TaskSubmission] = {}
    if task_ids:
        rows = db.execute(
            select(TaskSubmission, TaskParticipant.task_id)
            .join(TaskParticipant, TaskSubmission.participant_id == TaskParticipant.id)
            .where(
                TaskParticipant.user_id == viewer.id,
                TaskSubmission.task_id.in_(task_ids),
                TaskSubmission.is_current.is_(True),
            )
        ).all()
        my_current = {task_id: submission for submission, task_id in rows}
    is_admin = _is_admin(viewer)
    items: list[CompetitionTaskRead] = []
    for task in tasks:
        submission = my_current.get(task.id)
        my_submission = (
            CompetitionTaskSubmissionSummary(
                id=submission.id,
                artifact_id=submission.artifact_id,
                artifact_title=submission.artifact.title,
                round_no=submission.round_no,
                status=submission.status,
                is_current=submission.is_current,
                submitted_at=submission.submitted_at,
            )
            if submission is not None
            else None
        )
        current_submission_count = None
        reviewed_count = None
        if is_admin:
            current_submission_count = int(
                db.scalar(
                    select(func.count())
                    .select_from(TaskSubmission)
                    .where(TaskSubmission.task_id == task.id, TaskSubmission.is_current.is_(True))
                )
                or 0
            )
            reviewed_count = int(
                db.scalar(
                    select(func.count())
                    .select_from(TaskSubmission)
                    .join(CompetitionReview, CompetitionReview.task_submission_id == TaskSubmission.id)
                    .where(TaskSubmission.task_id == task.id, TaskSubmission.is_current.is_(True))
                )
                or 0
            )
        items.append(
            CompetitionTaskRead(
                id=task.id,
                title=task.title,
                description=task.description,
                creator=UserSummary.model_validate(task.creator),
                status=task.status,
                required=bool(task.competition_required),
                sort_order=task.competition_sort_order or 0,
                max_score=task.competition_max_score,
                weight=task.competition_weight,
                deadline_at=task.deadline_at,
                effective_deadline_at=effective_deadline(task, competition),
                my_submission=my_submission,
                current_submission_count=current_submission_count,
                reviewed_count=reviewed_count,
                created_at=task.created_at,
                updated_at=task.updated_at,
            )
        )
    return items


def results_read(db: Session, competition: Competition) -> CompetitionResultsRead:
    rows = list(
        db.scalars(
            select(CompetitionResult)
            .options(
                joinedload(CompetitionResult.registration).joinedload(CompetitionRegistration.user),
                joinedload(CompetitionResult.publisher),
            )
            .where(CompetitionResult.competition_id == competition.id)
            .order_by(CompetitionResult.rank.asc(), CompetitionResult.total_score.desc(), CompetitionResult.id.asc())
        ).all()
    )
    first = rows[0] if rows else None
    return CompetitionResultsRead(
        competition_id=competition.id,
        published_by=UserSummary.model_validate(first.publisher) if first is not None else None,
        published_at=first.published_at if first is not None else None,
        items=[
            CompetitionResultRow(
                registration_id=row.registration_id,
                user=UserSummary.model_validate(row.registration.user),
                total_score=row.total_score,
                rank=row.rank,
                award=row.award,
            )
            for row in rows
        ],
    )
