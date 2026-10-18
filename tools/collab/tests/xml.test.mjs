// XML result-gate regression. Optional explicit source allows before/after comparison.
import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const source = process.env.COLLAB_AUDIT_TOOL_SOURCE
  ? path.resolve(process.env.COLLAB_AUDIT_TOOL_SOURCE)
  : path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const { parseXmlDocument } = await import(pathToFileURL(path.join(source, 'lib/xml.mjs')));
const { parseNunitResult } = await import(pathToFileURL(path.join(source, 'lib/commands.mjs')));
const attrs = 'total="1" passed="1" failed="0" skipped="0" result="Passed"';
const wrap = body => `<test-run ${attrs}>${body}</test-run>`;
const cases = [
  ['adjacent attributes', '<test-run total="1"passed="1" failed="0" skipped="0" result="Passed"/>', false],
  ['CDATA terminator in text', wrap(']]>'), false],
  ['double hyphen in comment', wrap('<!--bad--comment-->'), false],
  ['comment ends in hyphen', wrap('<!--bad--->'), false],
  ['raw control character', wrap('\u0001'), false],
  ['decimal control reference', wrap('&#1;'), false],
  ['hex surrogate reference', wrap('&#xD800;'), false],
  ['noncharacter reference', wrap('&#xFFFE;'), false],
  ['misplaced XML declaration', wrap('<?xml version="1.0"?>'), false],
  ['declaration after whitespace', ` <?xml version="1.0"?>${wrap('')}`, false],
  ['invalid declaration content', `<?xml anything?>${wrap('')}`, false],
  ['non-XML whitespace in declaration', `<?xml version\u00a0="1.0"?>${wrap('')}`, false],
  ['duplicate XML declaration', `<?xml version="1.0"?><?xml version="1.0"?>${wrap('')}`, false],
  ['reserved PI target', `<?XML version="1.0"?>${wrap('')}`, false],
  ['empty PI target', `<??>${wrap('')}`, false],
  ['unvalidated doctype rejected', `<!DOCTYPE garbage>${wrap('')}`, false],
  ['truncated document', `<test-run ${attrs}>`, false],
  ['nested tag mismatch', wrap('<x></y>'), false],
  ['duplicate root', `${wrap('')}${wrap('')}`, false],
  ['valid single quoted attributes', "<test-run total='1' passed='1' failed='0' skipped='0' result='Passed'/>", true],
  ['valid Unicode and entities', wrap('航班 &amp; &#x1F600; &#9;'), true],
  ['valid encoded text terminator', wrap(']]&gt;'), true],
  ['valid CDATA', wrap('<![CDATA[<details>&raw]]>'), true],
  ['valid declaration', `<?xml version="1.0" encoding="utf-8" standalone="yes"?>${wrap('')}`, true],
  ['valid PI and comments', `<?xml-stylesheet type="text/xsl"?><!--before-->${wrap('<?diagnostic value?><!--ok-->')}<!--after-->`, true],
  ['valid attribute whitespace', '<test-run\n total="1"\tpassed="1" failed="0" skipped="0" result="Passed" />', true],
];
let failed = 0;
for (const [name, xml, expected] of cases) {
  try {
    const parsed = parseXmlDocument(xml);
    assert.equal(parsed.ok, expected, JSON.stringify(parsed));
    const result = parseNunitResult(xml);
    assert.equal(!!result.counters, expected, JSON.stringify(result));
    console.log(`PASS ${name}`);
  } catch (e) { failed++; console.log(`FAIL ${name}: ${e.message}`); }
}
console.log(`SUMMARY ${cases.length-failed}/${cases.length} XML gates passed; ${failed} failed; source=${source}`);
process.exitCode = failed ? 1 : 0;
