import { fail, EXIT } from './exit.mjs';

const DEV_STATUS = new Set(['todo', 'developing', 'ready_for_review', 'rework']);
const REVIEW_STATUS = new Set([
  'not_run',
  'queued',
  'running',
  'passed',
  'failed',
  'blocked',
  'cancelled',
  'interrupted',
]);
const FRESHNESS = new Set(['current', 'stale', 'unknown']);
const ACTORS = new Set(['cursor', 'codex', 'user', 'system']);
const TASK_ID_RE = /^[A-Za-z][A-Za-z0-9._-]{0,63}$/;
const ISO_RE = /^\d{4}-\d{2}-\d{2}T/;

function req(obj, fields, label) {
  for (const f of fields) {
    if (obj[f] === undefined || obj[f] === null || obj[f] === '') {
      fail(EXIT.USAGE, `${label} missing required field: ${f}`);
    }
  }
}

function assertType(cond, message) {
  if (!cond) fail(EXIT.USAGE, message);
}

export function validateTask(task) {
  req(task, [
    'schemaVersion',
    'projectId',
    'taskId',
    'title',
    'revision',
    'devStatus',
    'reviewStatus',
    'scope',
    'acceptanceCriteria',
    'createdAt',
    'updatedAt',
  ], 'task');
  assertType(task.schemaVersion === 1, 'task.schemaVersion must be number 1');
  assertType(typeof task.projectId === 'string', 'task.projectId must be string');
  assertType(typeof task.taskId === 'string' && TASK_ID_RE.test(task.taskId), 'task.taskId invalid');
  assertType(typeof task.title === 'string', 'task.title must be string');
  assertType(Number.isInteger(task.revision) && task.revision >= 1, 'task.revision must be positive integer');
  assertType(DEV_STATUS.has(task.devStatus), `invalid devStatus: ${task.devStatus}`);
  assertType(REVIEW_STATUS.has(task.reviewStatus), `invalid reviewStatus: ${task.reviewStatus}`);
  assertType(Array.isArray(task.scope), 'task.scope must be array');
  assertType(task.scope.every(s => typeof s === 'string'), 'scope entries must be strings');
  assertType(Array.isArray(task.acceptanceCriteria), 'task.acceptanceCriteria must be array');
  assertType(task.acceptanceCriteria.length > 0, 'task.acceptanceCriteria must be non-empty');
  for (const ac of task.acceptanceCriteria) {
    assertType(ac && typeof ac === 'object', 'acceptanceCriteria entries must be objects');
    assertType(typeof ac.id === 'string' && ac.id.length > 0, 'acceptanceCriteria.id required');
    assertType(typeof ac.text === 'string' && ac.text.length > 0, 'acceptanceCriteria.text required');
  }
  assertType(new Set(task.acceptanceCriteria.map(a => a.id)).size === task.acceptanceCriteria.length, 'acceptanceCriteria IDs must be unique');
  if (task.dependencies != null) assertType(Array.isArray(task.dependencies) && task.dependencies.every(id => typeof id === 'string' && TASK_ID_RE.test(id)), 'dependencies must be task IDs');
  for (const field of ['snapshotRoots', 'snapshotFiles']) if (task[field] != null) {
    assertType(Array.isArray(task[field]) && task[field].every(p => typeof p === 'string' && p &&
      !/^(?:[\\/]|[A-Za-z]:)/.test(p) && !p.split(/[\\/]/).some(s => s === '..' || s === '.')), `${field} must contain relative project paths`);
  }
  assertType(typeof task.createdAt === 'string' && ISO_RE.test(task.createdAt), 'task.createdAt must be ISO datetime');
  assertType(typeof task.updatedAt === 'string' && ISO_RE.test(task.updatedAt), 'task.updatedAt must be ISO datetime');
  if (task.freshness) assertType(FRESHNESS.has(task.freshness), `invalid freshness: ${task.freshness}`);
  return true;
}

const ID_RE = /^[A-Z][A-Z0-9]*(?:-[A-Za-z0-9]+)+$/;
const REL_PATH_RE = /^[A-Za-z0-9_./ -]+$/;
const SEVERITY = new Set(['minor', 'major', 'critical', 'blocker']);
const ISSUE_STATUS = new Set(['open', 'fixing', 'fixed', 'verified', 'closed', 'wontfix']);

const REVERSE_REVIEW_ACTIONS = new Set(['review-start', 'review-run', 'review-evidence', 'review-finish', 'review-cancel']);
/** A local, explicit role declaration; not a credential or OS authentication. */
export function validateReviewerAuthorization(g) {
  const hash = s => typeof s === 'string' && /^[0-9a-f]{64}$/.test(s);
  const valid = g && typeof g === 'object' && !Array.isArray(g) &&
    g.schemaVersion === 1 && g.grantedBy === 'user' && g.actor === 'cursor' &&
    g.developerActor === 'codex' && typeof g.projectId === 'string' && !!g.projectId &&
    typeof g.taskId === 'string' && TASK_ID_RE.test(g.taskId) &&
    typeof g.deliveryId === 'string' && ID_RE.test(g.deliveryId) &&
    typeof g.statement === 'string' && !!g.statement.trim() &&
    Array.isArray(g.actions) && g.actions.length > 0 && g.actions.every(a => REVERSE_REVIEW_ACTIONS.has(a)) &&
    Array.isArray(g.profiles) && g.profiles.length > 0 && g.profiles.every(p => typeof p === 'string' && /^[A-Za-z0-9_-]+$/.test(p)) &&
    ['implementation', 'verification', 'contract'].every(p => hash(g.subjectFingerprints?.[p]));
  if (!valid) fail(EXIT.FORBIDDEN, 'invalid scoped reverse-review authorization');
  return true;
}

export function validateDelivery(d) {
  // Policy check first: a developer must never self-declare independent acceptance.
  if (d && d.independentReviewPassed === true) {
    fail(EXIT.FORBIDDEN, 'developer must not set independentReviewPassed=true');
  }
  req(d, [
    'schemaVersion',
    'deliveryId',
    'taskId',
    'taskRevision',
    'actor',
    'tool',
    'summary',
    'files',
    'selfTest',
    'git',
    'fingerprints',
    'snapshotPath',
    'createdAt',
  ], 'delivery');
  assertType(d.schemaVersion === 1, 'delivery.schemaVersion must be 1');
  assertType(typeof d.deliveryId === 'string' && ID_RE.test(d.deliveryId), 'delivery.deliveryId invalid');
  assertType(typeof d.taskId === 'string' && TASK_ID_RE.test(d.taskId), 'delivery.taskId invalid');
  assertType(Number.isInteger(d.taskRevision) && d.taskRevision >= 1, 'delivery.taskRevision must be positive integer');
  const scopedException = d.actor === 'codex' && d.developmentException === true &&
    d.authorization?.grantedBy === 'user' && d.authorization.actor === 'codex' &&
    d.authorization.taskId === d.taskId && d.authorization.actions?.includes('deliver') &&
    /^[0-9a-f]{64}$/.test(d.authorization.sha256 || '');
  assertType(d.actor === 'cursor' || scopedException, 'only Cursor or a scoped user-authorized developer may create deliveries');
  assertType(typeof d.tool === 'string', 'delivery.tool must be string');
  assertType(typeof d.summary === 'string', 'delivery.summary must be string');
  assertType(d.files && typeof d.files === 'object' && !Array.isArray(d.files), 'delivery.files must be object');
  assertType(d.selfTest && typeof d.selfTest === 'object', 'delivery.selfTest must be object');
  assertType(d.git && typeof d.git === 'object', 'delivery.git must be object');
  assertType(
    d.fingerprints && typeof d.fingerprints === 'object' && !Array.isArray(d.fingerprints),
    'delivery.fingerprints must be object',
  );
  for (const part of ['implementation', 'verification', 'contract']) {
    const fp = d.fingerprints[part];
    assertType(fp && typeof fp === 'object', `delivery.fingerprints.${part} must be object`);
    assertType(typeof fp.fingerprint === 'string' && /^[0-9a-f]{64}$/.test(fp.fingerprint),
      `delivery.fingerprints.${part}.fingerprint must be sha256 hex`);
  }
  assertType(
    typeof d.snapshotPath === 'string' && REL_PATH_RE.test(d.snapshotPath),
    'delivery.snapshotPath must be relative path string',
  );
  assertType(typeof d.createdAt === 'string' && ISO_RE.test(d.createdAt), 'delivery.createdAt must be ISO datetime');
  return true;
}

export function validateReview(r) {
  req(r, [
    'schemaVersion',
    'reviewId',
    'deliveryId',
    'taskId',
    'taskRevision',
    'actor',
    'status',
    'createdAt',
  ], 'review');
  assertType(r.schemaVersion === 1, 'review.schemaVersion must be 1');
  assertType(typeof r.reviewId === 'string' && ID_RE.test(r.reviewId), 'review.reviewId invalid');
  assertType(typeof r.deliveryId === 'string' && ID_RE.test(r.deliveryId), 'review.deliveryId invalid');
  assertType(typeof r.taskId === 'string' && TASK_ID_RE.test(r.taskId), 'review.taskId invalid');
  assertType(Number.isInteger(r.taskRevision) && r.taskRevision >= 1, 'review.taskRevision must be positive integer');
  if (r.actor === 'cursor') {
    validateReviewerAuthorization(r.authorization);
    assertType(r.reviewException === true && /^[0-9a-f]{64}$/.test(r.authorization.sha256 || ''), 'Cursor review requires a recorded scoped authorization hash');
    assertType(r.authorization.deliveryId === r.deliveryId && r.authorization.taskId === r.taskId &&
      r.authorization.profiles.includes(r.profile) && r.authorization.actions.includes('review-start') &&
      ['implementation', 'verification', 'contract'].every(p => r.authorization.subjectFingerprints[p] === r.subjectFingerprints?.[p]), 'review authorization does not match review subject');
    assertType(r.independent === true && r.reviewKind === 'independent', 'authorized reverse review must retain independent reviewer identity');
  } else if (r.actor !== 'codex' && r.actor !== 'user') {
    fail(EXIT.FORBIDDEN, 'reviews must be authored by actor=codex (or user-authorized exception)');
  }
  assertType(REVIEW_STATUS.has(r.status), `invalid review status: ${r.status}`);
  assertType(typeof r.createdAt === 'string' && ISO_RE.test(r.createdAt), 'review.createdAt must be ISO datetime');
  if (r.finishedAt != null) {
    assertType(typeof r.finishedAt === 'string' && ISO_RE.test(r.finishedAt), 'review.finishedAt must be ISO datetime');
  }
  if (r.assertions != null) {
    for (const k of ['total', 'failed', 'skipped']) {
      assertType(
        Number.isInteger(r.assertions[k]) && r.assertions[k] >= 0,
        `review.assertions.${k} must be non-negative integer`,
      );
    }
  }
  return true;
}

export function validateIssue(i) {
  req(i, [
    'schemaVersion',
    'issueId',
    'taskId',
    'deliveryId',
    'title',
    'expected',
    'actual',
    'severity',
    'status',
    'createdAt',
  ], 'issue');
  assertType(i.schemaVersion === 1, 'issue.schemaVersion must be 1');
  assertType(typeof i.issueId === 'string' && ID_RE.test(i.issueId), 'issue.issueId invalid');
  assertType(typeof i.taskId === 'string' && TASK_ID_RE.test(i.taskId), 'issue.taskId invalid');
  assertType(typeof i.deliveryId === 'string' && ID_RE.test(i.deliveryId), 'issue.deliveryId invalid');
  assertType(typeof i.title === 'string', 'issue.title must be string');
  assertType(typeof i.expected === 'string', 'issue.expected must be string');
  assertType(typeof i.actual === 'string', 'issue.actual must be string');
  assertType(SEVERITY.has(i.severity), `invalid issue severity: ${i.severity}`);
  assertType(ISSUE_STATUS.has(i.status), `invalid issue status: ${i.status}`);
  assertType(typeof i.createdAt === 'string' && ISO_RE.test(i.createdAt), 'issue.createdAt must be ISO datetime');
  return true;
}

export function validateEvent(e) {
  req(e, ['schemaVersion', 'eventId', 'projectId', 'revision', 'at', 'actor', 'action'], 'event');
  assertType(e.schemaVersion === 1, 'event.schemaVersion must be 1');
  assertType(typeof e.eventId === 'string' && ID_RE.test(e.eventId), 'event.eventId invalid');
  assertType(typeof e.projectId === 'string' && e.projectId.length > 0, 'event.projectId must be string');
  assertType(Number.isInteger(e.revision) && e.revision >= 1, 'event.revision must be positive integer');
  assertType(typeof e.at === 'string' && ISO_RE.test(e.at), 'event.at must be ISO datetime');
  assertType(ACTORS.has(e.actor), `invalid actor: ${e.actor}`);
  assertType(typeof e.action === 'string' && /^[a-z][a-z-]*$/.test(e.action), 'event.action must be kebab-case string');
  return true;
}

export function assertRoleAction(actor, action) {
  const allowed = {
    cursor: new Set([
      'ack',
      'claim',
      'deliver',
      'release',
      'sync',
      'status',
      'doctor',
      'init',
      'watch',
      'task-add',
      'tasks',
      'gate',
      'taskbook-baseline',
      'hook',
      'hooks-status',
    ]),
    codex: new Set([
      'ack',
      'review-start',
      'review-run',
      'review-finish',
      'review-cancel',
      'review-recover',
      'sync',
      'status',
      'doctor',
      'release-review',
      'task-add',
      'tasks',
      'gate',
      'taskbook-baseline',
      'taskbook-check',
      'hook',
      'hooks-status',
    ]),
    user: new Set([
      'init',
      'doctor',
      'status',
      'sync',
      'ack',
      'unlock-collab',
      'review-cancel',
      'review-recover',
      'task-add',
      'tasks',
      'gate',
      'taskbook-baseline',
      'taskbook-check',
      'hook',
      'hooks-status',
    ]),
    system: new Set(['hook', 'hooks-status', 'gate', 'tasks']),
  };
  if (!allowed[actor]?.has(action)) {
    fail(EXIT.FORBIDDEN, `actor=${actor} is not allowed to run ${action}`);
  }
}

/** Stable acceptance-contract payload (excludes operational fields). */
export function stableContractPayload(task) {
  return {
    taskId: task.taskId,
    title: task.title,
    scope: task.scope,
    acceptanceCriteria: task.acceptanceCriteria,
    dependencies: task.dependencies || [],
    sourceTaskBook: task.sourceTaskBook || null,
    readOnlyReference: !!task.readOnlyReference,
  };
}
