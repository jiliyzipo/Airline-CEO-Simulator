/**
 * Minimal, strict, zero-dependency XML well-formedness parser.
 *
 * Why not a regex: a regex can match the opening `<test-run ...>` of a file that
 * was truncated mid-write, so "attributes found" never proved the document was a
 * complete result. This parser only returns a tree when the whole byte string is
 * one well-formed XML document.
 *
 * Supported result-file subset: XML 1.0, ASCII markup names, declarations,
 * comments, processing instructions, CDATA and predefined/numeric entities.
 * DTDs and XML 1.1 are deliberately rejected. This is not a general-purpose
 * validating XML processor. Test result files do not require those features.
 */

const NAME_START = /[A-Za-z_:]/;
const NAME_CHAR = /[A-Za-z0-9_:.\-]/;

function isWs(ch) {
  return ch === ' ' || ch === '\t' || ch === '\n' || ch === '\r';
}

// XML 1.0 §2.2 Char (also applies to numeric character references).
function isXmlChar(cp) {
  return cp === 9 || cp === 10 || cp === 13 ||
    (cp >= 0x20 && cp <= 0xd7ff) ||
    (cp >= 0xe000 && cp <= 0xfffd) ||
    (cp >= 0x10000 && cp <= 0x10ffff);
}

function decodeEntities(raw, where) {
  let out = '';
  for (let i = 0; i < raw.length; i++) {
    const ch = raw[i];
    if (ch !== '&') {
      out += ch;
      continue;
    }
    const semi = raw.indexOf(';', i + 1);
    if (semi < 0) return { error: `unterminated entity reference in ${where}` };
    const ent = raw.slice(i + 1, semi);
    if (ent === 'amp') out += '&';
    else if (ent === 'lt') out += '<';
    else if (ent === 'gt') out += '>';
    else if (ent === 'quot') out += '"';
    else if (ent === 'apos') out += "'";
    else if (/^#\d+$/.test(ent)) {
      const cp = Number(ent.slice(1));
      if (!Number.isSafeInteger(cp) || !isXmlChar(cp)) {
        return { error: `invalid character reference &${ent}; in ${where}` };
      }
      out += String.fromCodePoint(cp);
    } else if (/^#x[0-9a-fA-F]+$/.test(ent)) {
      const cp = Number.parseInt(ent.slice(2), 16);
      if (!Number.isSafeInteger(cp) || !isXmlChar(cp)) {
        return { error: `invalid character reference &${ent}; in ${where}` };
      }
      out += String.fromCodePoint(cp);
    } else {
      return { error: `unknown entity &${ent}; in ${where}` };
    }
    i = semi;
  }
  return { value: out };
}

/**
 * Parse one XML document.
 * @returns {{ok: true, root: object} | {ok: false, reason: string, at: number}}
 */
export function parseXmlDocument(input) {
  if (typeof input !== 'string') {
    return { ok: false, reason: 'document is not a string', at: 0 };
  }
  let s = input;
  if (s.charCodeAt(0) === 0xfeff) s = s.slice(1);
  if (s.trim().length === 0) return { ok: false, reason: 'document is empty', at: 0 };
  for (let at = 0; at < s.length;) {
    const cp = s.codePointAt(at);
    if (!isXmlChar(cp)) {
      return { ok: false, reason: `invalid XML 1.0 character U+${cp.toString(16)}`, at };
    }
    at += cp > 0xffff ? 2 : 1;
  }

  let i = 0;
  const bad = (reason, at = i) => ({ ok: false, reason, at });

  const skipWs = () => {
    while (i < s.length && isWs(s[i])) i++;
  };
  const readName = () => {
    if (i >= s.length || !NAME_START.test(s[i])) return null;
    const start = i;
    i++;
    while (i < s.length && NAME_CHAR.test(s[i])) i++;
    return s.slice(start, i);
  };
  const skipComment = () => {
    // i points at "<!--"
    const end = s.indexOf('-->', i + 4);
    if (end < 0) return bad('unterminated comment');
    const body = s.slice(i + 4, end);
    if (body.includes('--') || body.endsWith('-')) return bad('invalid double hyphen in comment');
    i = end + 3;
    return { ok: true };
  };
  const skipPi = () => {
    const start = i;
    i += 2;
    const target = readName();
    if (!target) return bad('processing instruction target expected');
    const end = s.indexOf('?>', i);
    if (end < 0) return bad('unterminated processing instruction');
    const body = s.slice(i, end);
    if (body.length && !isWs(body[0])) return bad('processing instruction data requires whitespace');
    if (target.toLowerCase() === 'xml') {
      if (start !== 0 || target !== 'xml') return bad('XML declaration must appear once at document start', start);
      // XMLDecl: version first, optional encoding, then optional standalone.
      const declaration = /^[ \t\r\n]+version[ \t\r\n]*=[ \t\r\n]*(["'])1\.0\1(?:[ \t\r\n]+encoding[ \t\r\n]*=[ \t\r\n]*(["'])[A-Za-z][A-Za-z0-9._-]*\2)?(?:[ \t\r\n]+standalone[ \t\r\n]*=[ \t\r\n]*(["'])(?:yes|no)\3)?[ \t\r\n]*$/;
      if (!declaration.test(body)) return bad('invalid or unsupported XML 1.0 declaration', start);
    }
    i = end + 2;
    return { ok: true };
  };
  const parseElement = (depth = 0) => {
    if (depth >= 256) return bad('result XML exceeds supported element nesting depth (256)');
    // i points at '<'
    i++;
    const name = readName();
    if (!name) return bad('element name expected after "<"');
    const attrs = Object.create(null);
    const labels = Object.create(null);
    for (;;) {
      const beforeWs = i;
      skipWs();
      const separated = i > beforeWs;
      if (i >= s.length) return bad(`unterminated start tag <${name}>`);
      if (s[i] === '>') {
        i++;
        break;
      }
      if (s[i] === '/' && s[i + 1] === '>') {
        i += 2;
        return {
          ok: true,
          node: { name, attrs, labels, children: [], text: '' },
        };
      }
      if (!separated) return bad(`attribute in <${name}> must be preceded by whitespace`);
      const attr = readName();
      if (!attr) return bad(`malformed attribute in <${name}>`);
      skipWs();
      if (s[i] !== '=') return bad(`attribute "${attr}" has no value`);
      i++;
      skipWs();
      const quote = s[i];
      if (quote !== '"' && quote !== "'") {
        return bad(`attribute "${attr}" value is not quoted`);
      }
      const close = s.indexOf(quote, i + 1);
      if (close < 0) return bad(`unterminated attribute value for "${attr}"`);
      const rawValue = s.slice(i + 1, close);
      if (rawValue.includes('<')) return bad(`attribute "${attr}" contains '<'`);
      const decoded = decodeEntities(rawValue, `attribute ${attr}`);
      if (decoded.error) return bad(decoded.error);
      if (attr in attrs) return bad(`duplicate attribute "${attr}" in <${name}>`);
      attrs[attr] = decoded.value;
      labels[attr] = rawValue;
      i = close + 1;
    }

    const node = { name, attrs, labels, children: [], text: '' };
    let text = '';
    for (;;) {
      if (i >= s.length) return bad(`element <${name}> is not closed`);
      if (s[i] === '<') {
        if (s.startsWith('</', i)) {
          i += 2;
          const closeName = readName();
          if (closeName === null) return bad(`malformed closing tag for <${name}>`);
          if (closeName !== name) {
            return bad(`closing tag </${closeName}> does not match <${name}>`);
          }
          skipWs();
          if (s[i] !== '>') return bad(`malformed closing tag </${closeName}>`);
          i++;
          node.text = text;
          return { ok: true, node };
        }
        if (s.startsWith('<!--', i)) {
          const r = skipComment();
          if (!r.ok) return r;
          continue;
        }
        if (s.startsWith('<![CDATA[', i)) {
          const end = s.indexOf(']]>', i + 9);
          if (end < 0) return bad('unterminated CDATA section');
          text += s.slice(i + 9, end);
          i = end + 3;
          continue;
        }
        if (s.startsWith('<?', i)) {
          const r = skipPi();
          if (!r.ok) return r;
          continue;
        }
        if (s.startsWith('<!', i)) {
          return bad('unexpected markup declaration inside an element');
        }
        const child = parseElement(depth + 1);
        if (!child.ok) return child;
        node.children.push(child.node);
        continue;
      }
      const next = s.indexOf('<', i);
      const chunk = s.slice(i, next < 0 ? s.length : next);
      if (chunk.includes(']]>')) return bad("']]>' is forbidden in ordinary character data");
      const decoded = decodeEntities(chunk, `text of <${name}>`);
      if (decoded.error) return bad(decoded.error);
      text += decoded.value;
      i = next < 0 ? s.length : next;
    }
  };

  // Prolog
  for (;;) {
    skipWs();
    if (i >= s.length) return bad('document has no root element');
    if (s.startsWith('<!--', i)) {
      const r = skipComment();
      if (!r.ok) return r;
      continue;
    }
    if (s.startsWith('<?', i)) {
      const r = skipPi();
      if (!r.ok) return r;
      continue;
    }
    if (/^<!DOCTYPE/.test(s.slice(i, i + 9))) {
      return bad('DOCTYPE declarations are unsupported in result XML');
    }
    break;
  }
  if (s[i] !== '<') return bad('root element expected');
  const rootResult = parseElement();
  if (!rootResult.ok) return rootResult;

  // Epilog: only whitespace, comments and processing instructions
  for (;;) {
    skipWs();
    if (i >= s.length) break;
    if (s.startsWith('<!--', i)) {
      const r = skipComment();
      if (!r.ok) return r;
      continue;
    }
    if (s.startsWith('<?', i)) {
      const r = skipPi();
      if (!r.ok) return r;
      continue;
    }
    return bad('content after the root element (multiple roots or trailing data)');
  }

  return { ok: true, root: rootResult.node };
}

/** Depth-first collection of every element with the given (case-insensitive) name. */
export function collectElements(node, name) {
  const wanted = String(name).toLowerCase();
  const out = [];
  const walk = (n) => {
    if (n.name.toLowerCase() === wanted) out.push(n);
    for (const c of n.children) walk(c);
  };
  if (node) walk(node);
  return out;
}
