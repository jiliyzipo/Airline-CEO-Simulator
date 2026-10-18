import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
export const TOOL_ROOT = path.resolve(__dirname, '..');
export const REPO_ROOT = path.resolve(TOOL_ROOT, '../..');

export function loadConfig(repoRoot = REPO_ROOT) {
  const cfgPath = path.join(repoRoot, 'tools/collab/config.json');
  const example = path.join(repoRoot, 'tools/collab/config.example.json');
  const raw = fs.existsSync(cfgPath)
    ? fs.readFileSync(cfgPath, 'utf8')
    : fs.readFileSync(example, 'utf8');
  let cfg;
  try { cfg = JSON.parse(raw); }
  catch { throw Object.assign(new Error('collaboration config is not valid JSON'), { exitCode: 2 }); }
  const relative = p => typeof p === 'string' && p.length > 0 && !path.isAbsolute(p) &&
    !p.includes(':') && !p.split(/[\\/]/).some(s => s === '..' || s === '.');
  if (cfg.schemaVersion !== 1 || typeof cfg.projectId !== 'string' || !cfg.projectId.trim() ||
      !['collaborationRoot', 'localRoot', 'snapshotRoot'].every(k => relative(cfg[k])) ||
      !cfg.profiles || typeof cfg.profiles !== 'object' || Array.isArray(cfg.profiles)) {
    throw Object.assign(new Error('invalid collaboration config: schema, projectId, relative roots or profiles'), { exitCode: 2 });
  }
  cfg._repoRoot = repoRoot;
  cfg._configPath = fs.existsSync(cfgPath) ? cfgPath : example;
  cfg._usingExample = !fs.existsSync(cfgPath);
  return cfg;
}

export function collabDirs(cfg) {
  const root = path.join(cfg._repoRoot, cfg.collaborationRoot);
  return {
    root,
    tasks: path.join(root, 'tasks'),
    deliveries: path.join(root, 'deliveries'),
    reviews: path.join(root, 'reviews'),
    events: path.join(root, 'events'),
    issues: path.join(root, 'issues'),
    verifications: path.join(root, 'verifications'),
    current: path.join(root, 'CURRENT.md'),
    nextCursor: path.join(root, 'NEXT_CURSOR.md'),
    nextCodex: path.join(root, 'NEXT_CODEX.md'),
    protocol: path.join(root, 'PROTOCOL.md'),
    readme: path.join(root, 'README.md'),
    state: path.join(root, 'state.json'),
  };
}

export function localDirs(cfg) {
  const root = path.join(cfg._repoRoot, cfg.localRoot);
  return {
    root,
    locks: path.join(root, 'locks'),
    tmp: path.join(root, 'tmp'),
    logs: path.join(root, 'logs'),
    ack: path.join(root, 'ack.json'),
  };
}

export function snapshotRoot(cfg) {
  return path.join(cfg._repoRoot, cfg.snapshotRoot || 'out/collab');
}

export function relPosix(repoRoot, absolutePath) {
  return path.relative(repoRoot, absolutePath).split(path.sep).join('/');
}
