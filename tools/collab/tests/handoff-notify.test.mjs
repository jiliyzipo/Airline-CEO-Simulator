import assert from 'node:assert/strict';
import { copyToClipboard, notifyHandoffReady } from '../lib/handoff-notify.mjs';

const results = [];
function test(id, title, fn) {
  try {
    fn();
    results.push({ id, title, ok: true });
    console.log(`ok ${id} ${title}`);
  } catch (e) {
    results.push({ id, title, ok: false, error: e.message });
    console.error(`FAIL ${id} ${title}: ${e.message}`);
  }
}

test('HN01', 'COLLAB_HANDOFF_NOTIFY=0 skips clipboard and chime', () => {
  const prev = process.env.COLLAB_HANDOFF_NOTIFY;
  process.env.COLLAB_HANDOFF_NOTIFY = '0';
  try {
    const out = notifyHandoffReady({
      nextCommand: 'node tools/collab/cli.mjs stage-review --delivery DEL-x --actor codex',
      deliveryId: 'DEL-x',
    });
    assert.equal(out.enabled, false);
    assert.equal(out.clipboard, false);
    assert.equal(out.chime, false);
  } finally {
    if (prev === undefined) delete process.env.COLLAB_HANDOFF_NOTIFY;
    else process.env.COLLAB_HANDOFF_NOTIFY = prev;
  }
});

test('HN02', 'copyToClipboard accepts empty as false', () => {
  assert.equal(copyToClipboard(''), false);
});

const failed = results.filter((r) => !r.ok);
console.log(JSON.stringify({ passed: results.length - failed.length, failed: failed.length, total: results.length }));
process.exit(failed.length ? 1 : 0);
