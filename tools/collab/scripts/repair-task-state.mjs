// One-off repair: the earlier refreshFreshness bug (wrong root set -> always
// "stale") downgraded tasks whose latest delivery has a real passed review.
// This restores the operational fields only; review/delivery history is untouched.
import path from 'node:path';
import { loadConfig } from '../lib/paths.mjs';
import { loadTask, saveTask } from '../lib/state.mjs';

const cfg = loadConfig();
const ids = ['C-ENG-COMPILE', 'C-ENG-SLOT', 'C-ENG-UNITY'];
for (const id of ids) {
  const task = loadTask(cfg, id);
  task.reviewStatus = 'passed';
  task.devStatus = 'ready_for_review';
  task.updatedAt = new Date().toISOString();
  saveTask(cfg, task);
  console.log(`${id}: reviewStatus=passed devStatus=ready_for_review`);
}
