/**
 * Local handoff helpers after a successful stage-close.
 *
 * Goal: cut the "copy this into Codex" friction without forging Codex's role.
 * - Put the stage-review one-liner on the clipboard.
 * - Fire a loud, detached chime so the developer hears it from another room.
 *
 * Always fail-open: clipboard/sound problems must never fail the delivery.
 * Disable with COLLAB_HANDOFF_NOTIFY=0 (tests set this).
 */
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHIME_SCRIPT = path.join(HERE, '..', 'scripts', 'handoff-ready-chime.ps1');

function notifyDisabled() {
  const v = String(process.env.COLLAB_HANDOFF_NOTIFY || '').trim().toLowerCase();
  return v === '0' || v === 'false' || v === 'off' || v === 'no';
}

/** Copy `text` to the system clipboard. Returns whether it likely succeeded. */
export function copyToClipboard(text) {
  const value = String(text || '');
  if (!value) return false;
  try {
    if (process.platform === 'win32') {
      const r = spawnSync(
        'powershell.exe',
        [
          '-NoProfile',
          '-ExecutionPolicy',
          'Bypass',
          '-Command',
          'Set-Clipboard -Value ([Console]::In.ReadToEnd())',
        ],
        { input: value, encoding: 'utf8', windowsHide: true, timeout: 15000 },
      );
      return r.status === 0;
    }
    if (process.platform === 'darwin') {
      const r = spawnSync('pbcopy', [], {
        input: value,
        encoding: 'utf8',
        timeout: 5000,
      });
      return r.status === 0;
    }
    const r = spawnSync('xclip', ['-selection', 'clipboard'], {
      input: value,
      encoding: 'utf8',
      timeout: 5000,
    });
    if (r.status === 0) return true;
    const r2 = spawnSync('xsel', ['--clipboard', '--input'], {
      input: value,
      encoding: 'utf8',
      timeout: 5000,
    });
    return r2.status === 0;
  } catch {
    return false;
  }
}

/**
 * Start the handoff chime without blocking the CLI.
 * On non-Windows hosts, falls back to a short console beep when available.
 */
export function playHandoffChime({ message } = {}) {
  try {
    if (process.platform === 'win32' && fs.existsSync(CHIME_SCRIPT)) {
      const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', CHIME_SCRIPT];
      if (message) args.push('-Message', String(message));
      const child = spawn('powershell.exe', args, {
        detached: true,
        stdio: 'ignore',
        windowsHide: true,
      });
      child.unref();
      return true;
    }
    // Best-effort short beep elsewhere; never block.
    if (typeof process.stdout?.write === 'function') {
      process.stdout.write('\x07');
    }
    return false;
  } catch {
    return false;
  }
}

/**
 * After a ready handoff: clipboard the Codex one-liner and ring the chime.
 * @returns {{ enabled: boolean, clipboard: boolean, chime: boolean, nextCommand: string|null }}
 */
export function notifyHandoffReady({ nextCommand, handoffFile, deliveryId } = {}) {
  if (notifyDisabled()) {
    return { enabled: false, clipboard: false, chime: false, nextCommand: nextCommand || null };
  }
  const cmd = String(nextCommand || '').trim();
  const clipboard = cmd ? copyToClipboard(cmd) : false;
  const spoken = deliveryId
    ? `交付 ${deliveryId} 已就绪，请交给 Codex 验收`
    : '交付已就绪，请交给 Codex 验收';
  const chime = playHandoffChime({ message: spoken });
  // Touch the handoff path so callers can log it; existence is not required.
  if (handoffFile && !fs.existsSync(handoffFile) && !path.isAbsolute(handoffFile)) {
    /* ignore — report path may be repo-relative */
  }
  return { enabled: true, clipboard, chime, nextCommand: cmd || null, host: os.hostname() };
}
