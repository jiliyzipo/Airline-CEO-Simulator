import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { collabDirs, localDirs } from './paths.mjs';
import { ensureDir, nowIso, readJson, writeFileAtomic, writeJsonAtomic } from './io.mjs';
import { EXIT, fail } from './exit.mjs';
import { listDeliveries, listIssues, listReviews } from './state.mjs';
import { freshnessSettles } from './basis.mjs';
import { gitTreeFingerprint, latestCleanCheckoutFor, treeFingerprintFor } from './cleancheckout.mjs';
import { gitMeta } from './fingerprint.mjs';

const sha = (s) => crypto.createHash('sha256').update(s, 'utf8').digest('hex');
const normalize = (s) => s.replace(/\s+/g, ' ').trim();
const escapeRe = s => String(s).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

const CHECKBOX_RE = /^(\s*-\s*)\[([ xX])\](.*)$/;

/**
 * Hash scheme for task-book clauses. Bumping it is a tool change, not a clause
 * edit: a baseline recorded under an older scheme must be re-recorded and must
 * never be reported as "the clause text changed" (that would be a false alarm
 * that blocks the completion gate forever after an upgrade).
 */
export const CLAUSE_SCHEME = 2;

function baselinePath(cfg) {
  return path.join(localDirs(cfg).root, 'taskbook', 'baseline.json');
}

function writeLogPath(cfg) {
  return path.join(localDirs(cfg).root, 'taskbook', 'writes.jsonl');
}

export function loadTaskBook(cfg) {
  const rel = cfg.taskBook?.path;
  if (!rel) fail(EXIT.USAGE, 'config.taskBook.path is required for task-book mapping');
  const abs = path.join(cfg._repoRoot, rel);
  if (!fs.existsSync(abs)) {
    fail(EXIT.CHECK_FAILED, `task book not found: ${rel}`, { taskBook: rel });
  }
  return { rel, abs, lines: fs.readFileSync(abs, 'utf8').split(/\r?\n/) };
}

/** Mapping declared in config for one task, or null when the task is unmapped. */
export function mappingFor(cfg, taskId) {
  const tasks = cfg.taskBook?.tasks;
  if (!tasks || typeof tasks !== 'object') return null;
  const entry = tasks[taskId];
  if (!entry) return null;
  return {
    taskId,
    sectionHeading: entry.sectionHeading || null,
    itemPrefix: entry.itemPrefix || taskId,
    requiresProfiles: Array.isArray(entry.requiresProfiles) ? entry.requiresProfiles : [],
    requiresCleanCheckout: !!entry.requiresCleanCheckout,
    totalMarker: entry.totalMarker || `**${taskId}`,
  };
}

/**
 * Locate the total checkbox and every sub-item for a task in the task book.
 * Refuses ambiguity instead of picking the first plausible line: a fuzzy match
 * could check the wrong clause.
 */
export function readClause(cfg, taskId, mapping = mappingFor(cfg, taskId)) {
  if (!mapping) {
    return { taskId, mapped: false, reason: `no taskBook.tasks mapping for ${taskId}` };
  }
  const { rel, lines } = (() => {
    try {
      return loadTaskBook(cfg);
    } catch (e) {
      return { rel: cfg.taskBook?.path || null, lines: null, error: e.message };
    }
  })();
  if (!lines) {
    return {
      taskId,
      mapped: true,
      file: rel,
      ok: false,
      reason: `task book unavailable: ${rel}`,
    };
  }

  const totalHits = [];
  lines.forEach((line, index) => {
    const m = line.match(CHECKBOX_RE);
    if (!m) return;
    const body = m[3].trim();
    if (!body.startsWith(mapping.totalMarker)) return;
    // `**T9` must not also match `**T9B`: the marker has to end at a boundary.
    const rest = body.slice(mapping.totalMarker.length);
    if (rest && /^[A-Za-z0-9_.-]/.test(rest)) return;
    totalHits.push({ index, checked: m[2].toLowerCase() === 'x', text: line });
  });
  if (totalHits.length === 0) {
    return {
      taskId,
      mapped: true,
      file: rel,
      ok: false,
      reason: `task book has no checkbox line starting with ${mapping.totalMarker}`,
    };
  }
  if (totalHits.length > 1) {
    return {
      taskId,
      mapped: true,
      file: rel,
      ok: false,
      reason: `task book has ${totalHits.length} checkbox lines starting with ${mapping.totalMarker}; refusing to guess`,
      matches: totalHits.map((h) => h.index + 1),
    };
  }
  const total = totalHits[0];

  // Section boundary: the heading declared in config, falling back to the task id.
  const heading = mapping.sectionHeading
    ? (line) => line.trim() === mapping.sectionHeading
    : (line) => new RegExp(`^##\\s+\\d+\\.\\s*${taskId}\\b`).test(line.trim());
  const headingHits = lines.map((line, index) => heading(line) ? index : -1).filter(i => i >= 0);
  const headingIndex = headingHits.length === 1 ? headingHits[0] : -1;
  if (headingIndex < 0) {
    return {
      taskId,
      mapped: true,
      file: rel,
      ok: false,
      reason: `section heading for ${taskId} not found (expected ${mapping.sectionHeading || `"## <n>. ${taskId}"`})`,
    };
  }
  let end = lines.length;
  for (let i = headingIndex + 1; i < lines.length; i++) {
    if (/^##\s/.test(lines[i])) {
      end = i;
      break;
    }
  }
  const itemRe = new RegExp(`^\\s*-\\s*\\[([ xX])\\]\\s*(${escapeRe(mapping.itemPrefix)}\\.\\d+)\\b(.*)$`);
  const items = [];
  for (let i = headingIndex + 1; i < end; i++) {
    const m = lines[i].match(itemRe);
    if (!m) continue;
    items.push({
      id: m[2],
      index: i,
      checked: m[1].toLowerCase() === 'x',
      text: normalize(lines[i]),
    });
  }
  if (new Set(items.map(i => i.id)).size !== items.length) return { taskId, mapped: true, file: rel, ok: false, reason: 'duplicate sub-item IDs; refusing ambiguous mapping' };
  // Check-state is deliberately excluded from the hash: checking a box is a
  // result, not a clause edit, and it must not invalidate the recorded baseline.
  const unstate = (s) => normalize(s).replace(/\[\s*[xX]\s*\]/g, '[ ]');
  const clauseHash = sha(
    JSON.stringify({
      total: unstate(total.text),
      items: items.map((i) => [i.id, unstate(i.text)]),
    }),
  );
  return {
    taskId,
    mapped: true,
    file: rel,
    ok: true,
    total: {
      index: total.index,
      line: total.index + 1,
      checked: total.checked,
      text: normalize(total.text),
    },
    items,
    section: { heading: lines[headingIndex].trim(), line: headingIndex + 1 },
    clauseHash,
  };
}

function loadBaseline(cfg) {
  return readJson(baselinePath(cfg), { schemaVersion: 1, tasks: {} });
}

/** Freeze the current clause text so later edits can be detected, not silently overwritten. */
export function recordBaseline(cfg, taskId) {
  const clause = readClause(cfg, taskId);
  if (!clause.mapped) fail(EXIT.USAGE, clause.reason);
  if (!clause.ok) fail(EXIT.CONFLICT, clause.reason, clause);
  const baseline = loadBaseline(cfg);
  baseline.schemaVersion = 1;
  baseline.tasks = baseline.tasks || {};
  const previous = baseline.tasks[taskId];
  baseline.tasks[taskId] = {
    clauseHash: clause.clauseHash,
    scheme: CLAUSE_SCHEME,
    file: clause.file,
    totalText: clause.total.text,
    itemIds: clause.items.map((i) => i.id),
    recordedAt: nowIso(),
  };
  ensureDir(path.dirname(baselinePath(cfg)));
  writeJsonAtomic(baselinePath(cfg), baseline);
  return {
    taskId,
    file: clause.file,
    clauseHash: clause.clauseHash,
    scheme: CLAUSE_SCHEME,
    previousHash: previous?.clauseHash || null,
    previousScheme: previous?.scheme ?? null,
    // A hash difference across schemes is a tool upgrade, not a clause edit.
    changedSincePrevious: !!previous && previous.scheme === CLAUSE_SCHEME && previous.clauseHash !== clause.clauseHash,
    schemeUpgraded: !!previous && previous.scheme !== CLAUSE_SCHEME,
    totalChecked: clause.total.checked,
    subItems: clause.items.length,
  };
}

function gateResult(taskId, parts) {
  const blockers = parts.filter((p) => !p.ok);
  return {
    taskId,
    ok: blockers.length === 0,
    checks: parts,
    blockers: blockers.map((b) => ({ id: b.id, detail: b.detail })),
  };
}

/**
 * Evaluate the completion gate for one task.
 *
 * For a task the tool manages (has deliveries), a current passed review is
 * required. For a read-only reference task the task book stays authoritative:
 * the tool only reports book state plus the extra conditions the manual names
 * (e.g. T02's unverified clean Git checkout), and refuses to check the total
 * clause unless everything is satisfied.
 */
export function computeTaskGate(cfg, taskId, options = {}) {
  const seen = options.seen || new Set();
  if (seen.has(taskId)) {
    return gateResult(taskId, [{ id: 'cycle', ok: false, detail: `dependency cycle at ${taskId}` }]);
  }
  const nextSeen = new Set([...seen, taskId]);
  const taskPath = path.join(collabDirs(cfg).tasks, `${taskId}.json`);
  const task = readJson(taskPath, null);
  const parts = [];
  const mapping = mappingFor(cfg, taskId);
  const completionTasks = Array.isArray(cfg.taskBook?.completionTasks)
    ? cfg.taskBook.completionTasks
    : Object.keys(cfg.taskBook?.tasks || {});
  const inCompletionSet = completionTasks.includes(taskId);

  parts.push({
    id: 'mapped',
    ok: !!mapping || !inCompletionSet,
    detail: mapping
      ? `mapped to ${cfg.taskBook.path}`
      : inCompletionSet
        ? 'no taskBook.tasks mapping for a completion-gate task'
        : 'not a task-book clause task; gate uses collaboration records only',
  });
  if (!mapping) {
    if (!task) {
      parts.push({ id: 'task-record', ok: false, detail: `task record missing: ${taskId}.json` });
      return gateResult(taskId, parts);
    }
    return finishTaskGate(cfg, task, taskId, parts, { mapping: null, seen: nextSeen });
  }

  const clause = readClause(cfg, taskId, mapping);

  parts.push({
    id: 'clause-locatable',
    ok: !!clause.ok,
    detail: clause.ok
      ? `total line ${clause.total.line}, ${clause.items.length} sub-items, section ${clause.section.heading}`
      : clause.reason,
  });
  if (!clause.ok) return gateResult(taskId, parts);

  const baseline = loadBaseline(cfg).tasks?.[taskId] || null;
  const baselineSchemeCurrent = !!baseline && baseline.scheme === CLAUSE_SCHEME;
  parts.push({
    id: 'clause-baseline',
    ok: baselineSchemeCurrent,
    detail: !baseline
      ? `no baseline; run: node tools/collab/cli.mjs taskbook-baseline --task ${taskId}`
      : baselineSchemeCurrent
        ? `baseline ${baseline.clauseHash.slice(0, 12)} recorded ${baseline.recordedAt} (scheme ${baseline.scheme})`
        : `baseline recorded under hash scheme ${baseline.scheme ?? 'legacy'} but the tool now uses ${CLAUSE_SCHEME}; this is a tool change, not a clause edit — re-record it: node tools/collab/cli.mjs taskbook-baseline --task ${taskId}`,
    legacyScheme: !!baseline && !baselineSchemeCurrent,
  });
  parts.push({
    id: 'clause-unchanged',
    ok: !!baseline && (!baselineSchemeCurrent || baseline.clauseHash === clause.clauseHash),
    detail: !baseline
      ? 'cannot compare without baseline'
      : !baselineSchemeCurrent
        ? 'comparison skipped: baseline predates the current hash scheme (re-record, do not treat as an edit)'
        : baseline.clauseHash === clause.clauseHash
          ? 'clause text matches baseline'
          : 'clause text changed since baseline; review the conflict before checking anything',
  });

  if (!task) {
    parts.push({ id: 'task-record', ok: false, detail: `task record missing: ${taskId}.json` });
    return gateResult(taskId, parts);
  }

  const unchecked = clause.items.filter((i) => !i.checked);
  parts.push({
    id: 'sub-items',
    ok: clause.items.length > 0 && unchecked.length === 0,
    detail:
      clause.items.length === 0
        ? 'no sub-items parsed; mapping is wrong'
        : unchecked.length === 0
          ? `all ${clause.items.length} sub-items checked`
          : `${unchecked.length} unchecked: ${unchecked.map((i) => i.id).join(', ')}`,
  });

  return finishTaskGate(cfg, task, taskId, parts, { mapping, clause, unchecked, seen: nextSeen });
}

/**
 * Shared tail of the gate: collaboration evidence plus the optional task-book
 * clause. Tasks without a book clause are judged only on collaboration records.
 */
function finishTaskGate(cfg, task, taskId, parts, { mapping, clause = null, unchecked = [], seen }) {
  const deliveries = listDeliveries(cfg);
  const reviews = listReviews(cfg);
  const issues = listIssues(cfg);
  const taskDeliveries = deliveries.filter((d) => d.taskId === taskId);
  const latestDelivery = [...taskDeliveries].reverse()[0] || null;
  const latestReviewForLatest = latestDelivery
    ? [...reviews].reverse().find((r) => r.deliveryId === latestDelivery.deliveryId) || null
    : null;
  if (!task.readOnlyReference) {
    parts.push({ id: 'independent-review', ok: latestReviewForLatest?.independent !== false,
      detail: latestReviewForLatest?.independent === false ? 'developer self-check is not independent acceptance' : 'separate developer and reviewer roles' });
    parts.push({
      id: 'current-passed-review',
      ok: latestReviewForLatest?.status === 'passed' && freshnessSettles(task.freshness),
      detail: latestReviewForLatest
        ? `latest review ${latestReviewForLatest.reviewId} ${latestReviewForLatest.status}; freshness=${task.freshness || 'unknown'}`
        : task.freshnessNote || 'no passed review for the latest delivery',
    });
    if ((mapping?.requiresProfiles || []).length > 0) {
      const latestByProfile = new Map();
      for (const r of reviews) {
        if (r.taskId === taskId && r.deliveryId === latestDelivery?.deliveryId && r.profile) {
          latestByProfile.set(r.profile, r);
        }
      }
      const covered = new Set([...latestByProfile].filter(([, r]) => r.status === 'passed' && r.independent !== false).map(([p]) => p));
      const missing = mapping.requiresProfiles.filter((p) => !covered.has(p));
      parts.push({
        id: 'required-profiles',
        ok: missing.length === 0,
        detail:
          missing.length === 0
            ? `covered: ${mapping.requiresProfiles.join(', ')}`
            : `missing independently passed profile(s): ${missing.join(', ')}`,
      });
    }
  } else {
    parts.push({
      id: 'reference-authority',
      ok: true,
      detail: 'read-only reference task: task book remains authoritative; the tool does not claim completion',
    });
  }

  if (mapping?.requiresCleanCheckout) {
    // Satisfied either by a delivery that recorded the flag, or by a clean
    // checkout verification bound to the commit the gate is judging. The
    // second path is what reference tasks need: they cannot be delivered, so a
    // delivery-only rule made this check impossible to pass by construction.
    const declared = latestDelivery?.git?.cleanGitCheckoutVerified === true;
    const head = latestDelivery?.git?.head || gitMeta(cfg._repoRoot).head || null;
    const tree = head ? treeFingerprintFor(cfg) : null;
    const record = tree ? latestCleanCheckoutFor(cfg, tree.fingerprint) : null;
    const ok = declared || !!record;
    parts.push({
      id: 'clean-git-checkout',
      ok,
      detail: ok
        ? declared
          ? 'a delivery recorded cleanGitCheckoutVerified=true'
          : `clean checkout ${record.ccvId} reproduces ${String(record.gitHead).slice(0, 12)} byte-for-byte (${record.fileCount} files)`
        : 'Git 干净检出未验证（没有匹配当前提交内容的 clean-checkout 记录）— blocks the total clause even if local runs pass',
    });
  }

  const openBlockers = issues.filter(
    (i) => i.taskId === taskId && i.status !== 'closed' && ['blocker', 'critical'].includes(i.severity),
  );
  parts.push({
    id: 'no-open-blockers',
    ok: openBlockers.length === 0,
    detail:
      openBlockers.length === 0
        ? 'no open blocker/critical issues'
        : `open: ${openBlockers.map((i) => i.issueId).join(', ')}`,
  });

  const deps = Array.isArray(task.dependencies) ? task.dependencies : [];
  if (deps.length > 0) {
    const depGates = deps.map((d) => computeTaskGate(cfg, d, { seen }));
    const bad = depGates.filter((g) => !g.ok);
    parts.push({
      id: 'dependencies',
      ok: bad.length === 0,
      detail:
        bad.length === 0
          ? `dependencies satisfied: ${deps.join(', ')}`
          : `blocked by: ${bad.map((g) => g.taskId).join(', ')}`,
    });
  }

  const gate = gateResult(taskId, parts);
  if (clause) {
    gate.clause = {
      file: clause.file,
      totalLine: clause.total.line,
      totalChecked: clause.total.checked,
      subItems: clause.items.length,
      checkedSubItems: clause.items.length - unchecked.length,
      clauseHash: clause.clauseHash,
    };
  }
  gate.latestDeliveryId = latestDelivery?.deliveryId || null;
  gate.latestReview = latestReviewForLatest
    ? { reviewId: latestReviewForLatest.reviewId, status: latestReviewForLatest.status }
    : null;
  return gate;
}

/** Tool-delivery clauses (§14 C01..C12) are read-only here: Codex fills them. */
function readToolClauses(cfg) {
  const rel = cfg.taskBook?.toolClauses?.path;
  if (!rel) return { configured: false, items: [] };
  const abs = path.join(cfg._repoRoot, rel);
  if (!fs.existsSync(abs)) return { configured: true, path: rel, items: [], error: 'file missing' };
  const lines = fs.readFileSync(abs, 'utf8').split(/\r?\n/);
  const items = [];
  const heading = cfg.taskBook.toolClauses.sectionHeading || '## 14. MVP 交付与验收清单';
  const sections = lines.map((line, index) => line.trim() === heading ? index : -1).filter((i) => i >= 0);
  if (sections.length !== 1) {
    return { configured: true, path: rel, items, error: `expected one tool-clause section ${heading}, found ${sections.length}` };
  }
  let end = lines.length;
  for (let i = sections[0] + 1; i < lines.length; i++) {
    if (/^#{1,2}\s/.test(lines[i])) { end = i; break; }
  }
  lines.forEach((line, index) => {
    if (index <= sections[0] || index >= end) return;
    const m = line.match(/^\s*-\s*\[([ xX])\]\s*(C\d{2})\s*[：:]/);
    if (!m) return;
    items.push({ id: m[2], checked: m[1].toLowerCase() === 'x', line: index + 1, text: normalize(line) });
  });
  const ids = items.map((i) => i.id);
  const expected = Array.from({ length: 12 }, (_, i) => `C${String(i + 1).padStart(2, '0')}`);
  const valid = ids.length === expected.length && new Set(ids).size === expected.length && expected.every((id) => ids.includes(id));
  return { configured: true, path: rel, items, ...(valid ? {} : { error: 'tool-clause section must contain C01 through C12 exactly once' }) };
}

export function computeOverallGate(cfg) {
  const tasks = Array.isArray(cfg.taskBook?.completionTasks)
    ? cfg.taskBook.completionTasks
    : Object.keys(cfg.taskBook?.tasks || {});
  const perTask = tasks.map((t) => computeTaskGate(cfg, t));
  const tool = readToolClauses(cfg);
  const openIssues = listIssues(cfg).filter((i) => i.status !== 'closed');
  const uncheckedTasks = perTask.filter((g) => {
    const clause = readClause(cfg, g.taskId);
    return clause.ok && !clause.total.checked;
  });
  const failingGates = perTask.filter((g) => !g.ok);
  const uncheckedTool = tool.items.filter((i) => !i.checked);
  const toolDocumentValid = !tool.configured || !tool.error;
  const ok =
    failingGates.length === 0 &&
    uncheckedTasks.length === 0 &&
    uncheckedTool.length === 0 &&
    openIssues.length === 0 && toolDocumentValid;
  return {
    ok,
    tasks: perTask,
    taskBook: {
      totalClauses: perTask.length,
      checked: perTask.filter((g) => { const c = readClause(cfg, g.taskId); return c.ok && c.total.checked; }).length,
      unchecked: uncheckedTasks.map((g) => g.taskId),
    },
    toolClauses: {
      configured: tool.configured,
      path: tool.path || null,
      total: tool.items.length,
      checked: tool.items.length - uncheckedTool.length,
      unchecked: uncheckedTool.map((i) => i.id),
      error: tool.error || null,
    },
    openIssues: openIssues.map((i) => ({ issueId: i.issueId, severity: i.severity, status: i.status })),
    blockers: [
      ...(!toolDocumentValid ? [{ id: 'tool:document', detail: tool.error }] : []),
      ...failingGates.map((g) => ({ id: `gate:${g.taskId}`, detail: g.blockers.map((b) => b.id).join(',') })),
      ...uncheckedTasks.map((g) => ({ id: `clause:${g.taskId}`, detail: 'total clause still unchecked in task book' })),
      ...uncheckedTool.map((i) => ({ id: `tool:${i.id}`, detail: 'tool delivery clause not yet checked' })),
      ...openIssues.map((i) => ({ id: `issue:${i.issueId}`, detail: i.status })),
    ],
  };
}

/**
 * Check the total clause for a task. Only allowed when the gate passes, the
 * clause is unique and unchanged since the baseline. Never rewrites sub-items.
 */
export function writeTotalCheckbox(cfg, taskId, options = {}) {
  const { rel, abs, lines } = loadTaskBook(cfg);
  const clause = readClause(cfg, taskId);
  if (!clause.ok) fail(EXIT.CONFLICT, clause.reason, clause);
  const gate = computeTaskGate(cfg, taskId);
  if (!gate.ok) {
    fail(
      EXIT.CHECK_FAILED,
      `completion gate for ${taskId} is not satisfied; refusing to check the task book`,
      { blockers: gate.blockers },
    );
  }
  if (clause.total.checked) return { taskId, file: rel, changed: false, alreadyChecked: true, line: clause.total.line };
  const line = lines[clause.total.index];
  const m = line.match(CHECKBOX_RE);
  if (!m) fail(EXIT.CONFLICT, `line ${clause.total.line} is no longer a checkbox`, { line });
  const rewritten = line.replace(/^(\s*-\s*)\[[ xX]\]/, '$1[x]');
  if (rewritten === line) fail(EXIT.CONFLICT, 'checkbox rewrite produced no change', { line });
  lines[clause.total.index] = rewritten;
  const nextText = lines.join('\n');
  writeFileAtomic(abs, nextText);

  const record = {
    at: nowIso(),
    taskId,
    actor: options.actor || 'codex',
    file: rel,
    line: clause.total.line,
    before: line,
    after: rewritten,
    clauseHash: clause.clauseHash,
  };
  ensureDir(path.dirname(writeLogPath(cfg)));
  fs.appendFileSync(writeLogPath(cfg), JSON.stringify(record) + '\n', 'utf8');
  return { taskId, file: rel, changed: true, line: clause.total.line, before: line, after: rewritten };
}

/** Compact per-task summary for `tasks`. */
export function taskBookSummary(cfg) {
  const ids = Object.keys(cfg.taskBook?.tasks || {});
  return ids.map((id) => {
    const clause = readClause(cfg, id);
    const baseline = loadBaseline(cfg).tasks?.[id] || null;
    return {
      taskId: id,
      mapped: clause.mapped,
      ok: !!clause.ok,
      file: clause.file || null,
      totalLine: clause.ok ? clause.total.line : null,
      totalChecked: clause.ok ? clause.total.checked : null,
      subItems: clause.ok ? clause.items.length : 0,
      checkedSubItems: clause.ok ? clause.items.filter((i) => i.checked).length : 0,
      clauseHash: clause.ok ? clause.clauseHash : null,
      baselineRecorded: !!baseline,
      baselineScheme: baseline?.scheme ?? null,
      clauseChanged: !!(
        clause.ok &&
        baseline &&
        baseline.scheme === CLAUSE_SCHEME &&
        baseline.clauseHash !== clause.clauseHash
      ),
      baselineStale: !!(clause.ok && baseline && baseline.scheme !== CLAUSE_SCHEME),
      reason: clause.ok ? null : clause.reason,
    };
  });
}

// Only revoke checkboxes this tool wrote. Imported/manual game references stay untouched.
export function reconcileManagedCheckboxes(cfg) {
  if (!fs.existsSync(writeLogPath(cfg))) return [];
  const records = fs.readFileSync(writeLogPath(cfg), 'utf8').trim().split(/\r?\n/).filter(Boolean).map(s => JSON.parse(s));
  const latest = new Map(records.map(r => [r.taskId, r]));
  const results = [];
  for (const [taskId, record] of latest) {
    if (record.revoked) continue;
    const task = readJson(path.join(collabDirs(cfg).tasks, `${taskId}.json`));
    if (!task || task.readOnlyReference) continue;
    const clause = readClause(cfg, taskId);
    if (!clause.ok || !clause.total.checked) continue;
    const gate = computeTaskGate(cfg, taskId);
    if (gate.ok) continue;
    if (clause.clauseHash !== record.clauseHash || normalize(record.after) !== clause.total.text) {
      results.push({ taskId, conflict: true, reason: 'manual clause edit: refusing automatic rewrite' });
      continue;
    }
    const book = loadTaskBook(cfg);
    const before = book.lines[clause.total.index];
    const after = before.replace(/^(\s*-\s*)\[[ xX]\]/, '$1[ ]');
    book.lines[clause.total.index] = after;
    writeFileAtomic(book.abs, book.lines.join('\n'));
    const revocation = { ...record, at: nowIso(), actor: 'system', before, after, revoked: true,
      blockers: gate.blockers, latestDeliveryId: gate.latestDeliveryId, latestReview: gate.latestReview };
    fs.appendFileSync(writeLogPath(cfg), JSON.stringify(revocation) + '\n');
    results.push({ taskId, revoked: true, blockers: gate.blockers });
  }
  return results;
}
