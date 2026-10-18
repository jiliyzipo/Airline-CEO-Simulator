import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { assertRoleAction, validateReviewerAuthorization, validateDelivery } from './schemas.mjs';
import { resolveTaskRoots } from './roots.mjs';
import { EXIT, fail } from './exit.mjs';

// A declared local role exception, not OS authentication. Never impersonate Cursor.
export function developerAuthorization(cfg, task, actor, action, file) {
  if (actor !== 'codex') { assertRoleAction(actor, action); return null; }
  if (!file) fail(EXIT.FORBIDDEN, 'Codex development requires --authorization <user-grant.json>');
  const bytes = fs.readFileSync(path.resolve(file));
  let grant;
  try { grant = JSON.parse(bytes.toString('utf8')); } catch { fail(EXIT.USAGE, 'authorization must be JSON'); }
  if (grant.schemaVersion !== 1 || grant.grantedBy !== 'user' || grant.actor !== actor ||
      grant.projectId !== cfg.projectId || grant.taskId !== task.taskId ||
      !Array.isArray(grant.actions) || !grant.actions.includes(action) ||
      typeof grant.statement !== 'string' || !grant.statement.trim() ||
      !Array.isArray(grant.allowedRoots) || !grant.allowedRoots.length) {
    fail(EXIT.FORBIDDEN, 'authorization does not cover this actor, task and action');
  }
  const clean = p => typeof p === 'string' && p && !path.isAbsolute(p) &&
    !p.split(/[\\/]/).some(s => s === '..' || s === '.') && !p.includes(':');
  const roots = resolveTaskRoots(cfg, task).implementationRoots;
  if (!grant.allowedRoots.every(clean) || !roots.every(r => grant.allowedRoots.some(a => r === a || r.startsWith(a + '/')))) {
    fail(EXIT.FORBIDDEN, 'task snapshot exceeds the authorized roots');
  }
  return { ...grant, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
}

/** Bind the exception to one Codex delivery, its immutable identity and the owner. */
export function reviewerAuthorization(cfg, delivery, actor, action, file, profile, review = null) {
  if (actor !== 'cursor') {
    assertRoleAction(actor, action === 'review-evidence' ? 'review-finish' : action);
    // user may administratively cancel; execution and verdict remain reviewer-owned.
    if (review && review.actor !== actor && !(actor === 'user' && action === 'review-cancel')) {
      fail(EXIT.FORBIDDEN, 'review operation must use its recorded reviewer actor');
    }
    return null;
  }
  if (!file) fail(EXIT.FORBIDDEN, 'Cursor reverse review requires --authorization <user-grant.json>');
  let bytes, grant;
  try { bytes = fs.readFileSync(path.resolve(file)); grant = JSON.parse(bytes.toString('utf8')); }
  catch { fail(EXIT.FORBIDDEN, 'review authorization is missing or invalid JSON'); }
  validateReviewerAuthorization(grant);
  validateDelivery(delivery);
  if (delivery.actor !== 'codex' || grant.developerActor !== delivery.actor ||
      grant.projectId !== cfg.projectId || grant.taskId !== delivery.taskId ||
      grant.deliveryId !== delivery.deliveryId || !grant.actions.includes(action) ||
      !grant.profiles.includes(profile) ||
      !['implementation', 'verification', 'contract'].every(p => grant.subjectFingerprints[p] === delivery.fingerprints[p].fingerprint)) {
    fail(EXIT.FORBIDDEN, 'reverse-review authorization does not cover this delivery, fingerprint, profile or action');
  }
  const recorded = { ...grant, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
  if (review && (review.actor !== actor || review.reviewException !== true ||
      review.authorization?.sha256 !== recorded.sha256)) {
    fail(EXIT.FORBIDDEN, 'review owner or pinned authorization changed');
  }
  return recorded;
}
