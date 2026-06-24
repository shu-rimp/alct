#!/usr/bin/env node
// 용어집(src/assets/glossary_data.json) 정규화·검증 — 머지 충돌을 줄이기 위한 단일 정렬 규칙.
//   node scripts/format-glossary.mjs --check   # CI: 검증 + 정규형 일치 확인(불일치 시 exit 1)
//   node scripts/format-glossary.mjs --write    # 로컬: 정렬·정규형으로 정리
//
// 정규형: 모든 객체 키를 코드포인트 순으로 정렬(Hangul은 ≈가나다, ICU 비의존 → 환경 무관 결정적),
//         2-space 들여쓰기, 별칭 배열은 한 줄, 표준 JSON(후행 쉼표 없음).
import { readFileSync, writeFileSync } from 'node:fs';

const FILE = 'src/assets/glossary_data.json';
// 닫힌 스키마 — 허용된 키/언어만 통과(오타·엉뚱한 필드 차단: 예 "common"→"commmon", "ja-JP"→"ja-JPP").
// 언어를 추가하려면 ALLOWED_LANGS에 코드를 추가하면 된다.
const ALLOWED_TOP = ['common', 'languages', 'version'];
const ALLOWED_LANGS = ['ja-JP', 'zh-CN'];
const mode = process.argv[2] ?? '--check';
// 줄바꿈은 LF로 정규화 — 정규형 비교가 플랫폼(autocrlf)에 흔들리지 않게. .gitattributes로도 LF 고정.
const raw = readFileSync(FILE, 'utf8').replace(/\r\n/g, '\n');

const errors = [];

// ── 중복 키 탐지 ─────────────────────────────────────────────
// JSON.parse는 중복 키를 조용히 덮어써 기여 항목이 유실되므로, 파싱 전에 raw에서 직접 잡는다.
function findDuplicateKeys(text) {
  const dups = [];
  const stack = []; // { isObject, keys:Set|null }
  let i = 0;
  const n = text.length;
  const skipString = () => { // text[i] === '"' 가정, 닫는 따옴표 다음으로 i 이동, 내용 반환
    let s = ''; i++;
    while (i < n) {
      const c = text[i];
      if (c === '\\') { s += text[i + 1] ?? ''; i += 2; continue; }
      if (c === '"') { i++; break; }
      s += c; i++;
    }
    return s;
  };
  while (i < n) {
    const c = text[i];
    if (c === '"') {
      const str = skipString();
      let j = i; while (j < n && /\s/.test(text[j])) j++;
      const top = stack[stack.length - 1];
      if (text[j] === ':' && top && top.isObject) {
        if (top.keys.has(str)) dups.push(str);
        else top.keys.add(str);
      }
      continue;
    }
    if (c === '{') stack.push({ isObject: true, keys: new Set() });
    else if (c === '[') stack.push({ isObject: false, keys: null });
    else if (c === '}' || c === ']') stack.pop();
    i++;
  }
  return dups;
}

const dups = findDuplicateKeys(raw);
for (const k of dups) errors.push(`duplicate key: "${k}"`);

// ── 파싱 ────────────────────────────────────────────────────
let data;
try {
  data = JSON.parse(raw);
} catch (e) {
  console.error(`::error::glossary_data.json invalid JSON — ${e.message}`);
  process.exit(1);
}

// ── 구조 검증 (클라이언트 GlossaryService.TryLoad 기대치) ──────
const checkTerms = (obj, where) => {
  if (obj === null || typeof obj !== 'object' || Array.isArray(obj)) {
    errors.push(`${where} must be an object`); return;
  }
  for (const [k, v] of Object.entries(obj)) {
    if (!Array.isArray(v) || !v.every(a => typeof a === 'string')) {
      errors.push(`${where}["${k}"] must be an array of strings`);
    }
  }
};
if (data === null || typeof data !== 'object' || Array.isArray(data)) {
  errors.push('top-level must be an object');
} else {
  // 알 수 없는 top-level 키 차단
  for (const k of Object.keys(data)) {
    if (!ALLOWED_TOP.includes(k)) errors.push(`unknown top-level key: "${k}" (허용: ${ALLOWED_TOP.join(', ')})`);
  }
  if (!data.languages || typeof data.languages !== 'object' || Array.isArray(data.languages)) {
    errors.push("missing or invalid 'languages' object");
  } else {
    for (const [lang, terms] of Object.entries(data.languages)) {
      if (!ALLOWED_LANGS.includes(lang)) errors.push(`unknown language: "${lang}" (허용: ${ALLOWED_LANGS.join(', ')})`);
      checkTerms(terms, `languages["${lang}"]`);
    }
  }
  if ('common' in data) checkTerms(data.common, 'common');
}

if (errors.length) {
  for (const e of errors) console.error(`::error::${e}`);
  process.exit(1);
}

// ── 언어 키 로스터 통일 ──────────────────────────────────────
// 어느 섹션(common·각 언어)에 추가된 키든 모든 섹션에 같은 키를 채운다(없으면 빈 배열).
// 빈 배열 = "그 언어 표기 아직 없음" 표시 → 기여자가 채울 곳이 한눈에 보이고, 한 곳만 추가해도 전 섹션에 전파.
const sections = [];
if (data.common && typeof data.common === 'object') sections.push(data.common);
for (const lang of Object.values(data.languages ?? {})) {
  if (lang && typeof lang === 'object') sections.push(lang);
}
const allKeys = new Set();
for (const s of sections) for (const k of Object.keys(s)) allKeys.add(k);
for (const s of sections) for (const k of allKeys) if (!(k in s)) s[k] = [];

// ── 정규형 직렬화 ────────────────────────────────────────────
// 키 정렬 + 별칭 배열 인라인 + 표준 JSON. JSON.stringify가 CJK를 이스케이프하지 않음(리터럴 유지).
function serialize(value, indent = 0) {
  const pad = '  '.repeat(indent);
  const pad1 = '  '.repeat(indent + 1);
  if (Array.isArray(value)) {
    return '[' + value.map(v => JSON.stringify(v)).join(', ') + ']';
  }
  if (value && typeof value === 'object') {
    const keys = Object.keys(value).sort();
    if (keys.length === 0) return '{}';
    const body = keys
      .map(k => `${pad1}${JSON.stringify(k)}: ${serialize(value[k], indent + 1)}`)
      .join(',\n');
    return `{\n${body}\n${pad}}`;
  }
  return JSON.stringify(value);
}

const formatted = serialize(data) + '\n';

if (mode === '--write') {
  writeFileSync(FILE, formatted);
  console.log(`formatted ${FILE}`);
} else if (mode === '--check') {
  if (raw !== formatted) {
    console.error('::error::glossary_data.json is not in canonical form. Run: node scripts/format-glossary.mjs --write');
    process.exit(1);
  }
  console.log('glossary_data.json OK (canonical)');
} else {
  console.error(`unknown mode: ${mode} (use --check or --write)`);
  process.exit(1);
}
