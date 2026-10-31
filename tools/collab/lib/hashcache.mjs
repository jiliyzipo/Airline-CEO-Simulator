// 内容哈希缓存（stat 键）：避免每次 sync 把没变过的源码再读一遍算 sha256。
//
// 背景：refreshFreshness 每个阶段都要为「最新交付仍是全树基线」的任务重算一遍
// Unity 源码树指纹。文件其实没变，但 readFileSync + sha256 每次都真做，单次
// 遍历 1.5s 左右，多个任务叠加就是几十秒。
//
// 复用条件：size / mtimeMs / ctimeMs 三者与上次记录完全一致。ctime 由文件系统在
// 内容或元数据变更时自动更新，普通工具（touch、copy、git checkout）都无法把它
// 保留成旧值，因此「三个字段都没变」等价于「内容没变」。缓存只替代内容读取，
// 目录遍历仍然每次真走，所以新增、删除、改名都会照常被发现。
//
// 位置：`<localRoot>/fingerprint-hash-cache.json`，与锁、日志同属机器本地目录，
// 既不会进交付快照，也不会进指纹（`.collab-local` 在排除名单里）。
import fs from 'node:fs';
import path from 'node:path';
import { localDirs } from './paths.mjs';
import { ensureDir, readJson, writeJsonAtomic } from './io.mjs';

const SCHEMA_VERSION = 1;
// 只保留最近两轮见过的条目：删掉的文件不会永久占坑，缓存不会无限长。
const KEEP_RUNS = 2;
const MAX_ENTRIES = 200000;

/** 一条缓存记录：[size, mtimeMs, ctimeMs, sha256, scanned, lastSeenRun]。 */
function entryFor(st, sha256, scanned, run) {
  return [st.size, st.mtimeMs, st.ctimeMs, sha256, scanned ? 1 : 0, run];
}

export class HashCache {
  constructor(file, raw = null) {
    this.file = file;
    this.run = Number(raw?.run || 0) + 1;
    this.entries = new Map(Object.entries(raw?.entries || {}));
    this.hits = 0;
    this.misses = 0;
    this.dirty = false;
  }

  static load(file) {
    const raw = readJson(file, null);
    const versionOk = raw && raw.schemaVersion === SCHEMA_VERSION && raw.entries && typeof raw.entries === 'object';
    return new HashCache(file, versionOk ? raw : null);
  }

  /**
   * 命中的唯一条件：三个 stat 字段逐一对上，且（需要做凭据扫描的文件）上次记录
   * 也是扫描过才入库的。扫描标记让「缓存命中」不会顺带跳过安全门禁。
   */
  lookup(absolute, st) {
    const entry = this.entries.get(absolute);
    if (!entry) {
      this.misses++;
      return null;
    }
    if (entry[0] !== st.size || entry[1] !== st.mtimeMs || entry[2] !== st.ctimeMs) {
      this.misses++;
      return null;
    }
    if (entry[5] !== this.run) {
      entry[5] = this.run;
      this.dirty = true;
    }
    this.hits++;
    return { bytes: entry[0], sha256: entry[3], scanned: entry[4] === 1 };
  }

  record(absolute, st, sha256, scanned) {
    const entry = entryFor(st, sha256, scanned, this.run);
    const prev = this.entries.get(absolute);
    if (
      !prev ||
      prev[0] !== entry[0] ||
      prev[1] !== entry[1] ||
      prev[2] !== entry[2] ||
      prev[3] !== entry[3] ||
      prev[4] !== entry[4]
    ) {
      this.entries.set(absolute, entry);
      this.dirty = true;
    } else if (prev[5] !== this.run) {
      prev[5] = this.run;
      this.dirty = true;
    }
  }

  /** 落盘；返回写出的统计，便于在报告里说明缓存到底有没有生效。 */
  save() {
    if (!this.dirty) return { written: false, entries: this.entries.size, dropped: 0 };
    const keepFrom = this.run - (KEEP_RUNS - 1);
    const kept = {};
    let dropped = 0;
    for (const [key, entry] of this.entries) {
      if (entry[5] < keepFrom) {
        dropped++;
        continue;
      }
      kept[key] = entry;
    }
    const keys = Object.keys(kept);
    if (keys.length > MAX_ENTRIES) {
      // 兜底：只保留本轮见过的条目，宁可下次重算也不要留下一个巨型 JSON。
      for (const key of keys) if (kept[key][5] !== this.run) delete kept[key];
    }
    ensureDir(path.dirname(this.file));
    writeJsonAtomic(this.file, { schemaVersion: SCHEMA_VERSION, run: this.run, entries: kept });
    this.dirty = false;
    return { written: true, entries: Object.keys(kept).length, dropped };
  }
}

let shared = null;
let sharedFile = null;
let exitHookInstalled = false;

/**
 * 进程级共享缓存：一次 CLI 调用里的 deliver / review-finish / sync 复用同一份
 * 内存映射，进程退出时统一落盘。`COLLAB_FINGERPRINT_CACHE=0` 可整体关闭，
 * 用于「缓存是否真的没改变指纹」的对照验证。
 */
export function hashCacheFor(cfg) {
  if (process.env.COLLAB_FINGERPRINT_CACHE === '0') return null;
  const file = path.join(localDirs(cfg).root, 'fingerprint-hash-cache.json');
  if (shared && sharedFile === file) return shared;
  shared = HashCache.load(file);
  sharedFile = file;
  // 一个进程只装一个退出钩子：夹具里每换一个仓库就换一个缓存文件，若每次都
  // process.once('exit') 会堆到十几层监听（实测已触发 MaxListeners 警告）。
  if (!exitHookInstalled) {
    exitHookInstalled = true;
    process.once('exit', () => {
      try {
        shared?.save();
      } catch {
        /* 缓存写不出只影响下次的速度，绝不能让退出码变脏 */
      }
    });
  }
  return shared;
}

/** 仅供测试：丢掉进程级单例，避免用例之间互相污染。 */
export function resetSharedHashCacheForTests() {
  shared = null;
  sharedFile = null;
}

export const HASH_CACHE_SCHEMA_VERSION = SCHEMA_VERSION;
