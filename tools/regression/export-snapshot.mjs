// 一次性工具：导出隔离快照供 RunUnityTests.ps1 跑 PlayMode（用户已关 Unity）。
// 不是交付流程的一部分；跑完即可删。
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const href = (rel) => pathToFileURL(path.join(process.cwd(), rel)).href;
const { exportStableSnapshot } = await import(href('tools/collab/lib/snapshot.mjs'));
const { IMPLEMENTATION_ROOTS, VERIFICATION_ROOTS } = await import(
  href('tools/collab/lib/fingerprint.mjs'),
);

const dest = process.argv[2];
if (!dest) {
  console.error('usage: node tools/regression/export-snapshot.mjs <dest>');
  process.exit(3);
}
const manifest = exportStableSnapshot(process.cwd(), dest, {
  roots: [...new Set([...IMPLEMENTATION_ROOTS, ...VERIFICATION_ROOTS])],
});
console.log(`snapshot ok: ${dest} files=${manifest.copiedFileCount}`);
