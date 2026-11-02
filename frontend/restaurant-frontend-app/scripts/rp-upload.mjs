import { readFileSync } from 'node:fs';
import { XMLParser } from 'fast-xml-parser';

const { RP_ENDPOINT, RP_API_KEY, RP_PROJECT, RP_LAUNCH, GITHUB_RUN_NUMBER } = process.env;
const file = process.argv[2] ?? 'test-results/junit.xml';

if (!RP_ENDPOINT || !RP_API_KEY || !RP_PROJECT || !RP_LAUNCH) {
  console.error('Set RP_ENDPOINT, RP_API_KEY, RP_PROJECT and RP_LAUNCH');
  process.exit(1);
}

const base = `${RP_ENDPOINT.replace(/\/$/, '')}/api/v1/${RP_PROJECT}`;

async function api(method, path, body) {
  const res = await fetch(base + path, {
    method,
    headers: { Authorization: `Bearer ${RP_API_KEY}`, 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status}: ${text}`);
  return text ? JSON.parse(text) : {};
}

const asArray = (v) => (v === undefined ? [] : Array.isArray(v) ? v : [v]);
const iso = (t) => new Date(t).toISOString();
const textOf = (v) =>
  typeof v === 'string' ? v : (v?.['#text'] ?? v?.['@_message'] ?? JSON.stringify(v));

const parser = new XMLParser({ ignoreAttributes: false, attributeNamePrefix: '@_' });
const xml = parser.parse(readFileSync(file, 'utf8'));
const suites = asArray(xml.testsuites?.testsuite ?? xml.testsuite);

const cases = suites.flatMap((s) => asArray(s.testcase));
const totalMs = cases.reduce((sum, c) => sum + Number(c['@_time'] ?? 0) * 1000, 0);
let cursor = Date.now() - totalMs - 5000 - cases.length;

const launch = await api('POST', '/launch', {
  name: RP_LAUNCH,
  startTime: iso(cursor - 1000),
  mode: 'DEFAULT',
  description: `GitHub run #${GITHUB_RUN_NUMBER ?? 'local'}`,
});
const launchUuid = launch.id;

let passed = 0;
let failed = 0;
let skipped = 0;

for (const s of suites) {
  const suite = await api('POST', '/item', {
    name: String(s['@_name'] ?? 'Karma tests').slice(0, 1000),
    type: 'suite',
    startTime: iso(cursor),
    launchUuid,
  });

  for (const c of asArray(s.testcase)) {
    const ms = Math.max(1, Math.round(Number(c['@_time'] ?? 0) * 1000));
    const item = await api('POST', `/item/${suite.id}`, {
      name: String(c['@_name'] ?? 'test').slice(0, 1000),
      type: 'step',
      startTime: iso(cursor),
      launchUuid,
    });

    const failure = c.failure ?? c.error;
    let status = 'passed';
    if (failure !== undefined) {
      status = 'failed';
      await api('POST', '/log', {
        launchUuid,
        itemUuid: item.id,
        time: iso(cursor + ms),
        level: 'error',
        message: textOf(failure).slice(0, 5000) || 'Test failed',
      });
    } else if (c.skipped !== undefined) {
      status = 'skipped';
    }

    if (status === 'passed') passed++;
    else if (status === 'failed') failed++;
    else skipped++;

    cursor += ms;
    await api('PUT', `/item/${item.id}`, { launchUuid, endTime: iso(cursor), status });
  }

  await api('PUT', `/item/${suite.id}`, { launchUuid, endTime: iso(cursor) });
}

await api('PUT', `/launch/${launchUuid}/finish`, { endTime: iso(Math.max(cursor, Date.now())) });
console.log(`Uploaded to ReportPortal: passed=${passed}, failed=${failed}, skipped=${skipped}`);