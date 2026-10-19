// Day 42 - Performance baseline (ADR-0037). Run via build/scripts/perf-baseline.sh, not directly.
//
// Three paths through the Gateway, each held at 1, 10, 25 and 50 virtual users for STEADY_SECONDS,
// one level after another (never overlapping, so each level's percentiles are its own):
//   health - GET /health            pure Gateway -> Api proxy cost, no auth, no database
//   read   - GET /api/reference/countries/IN   JWT validated at the Gateway and the Api + one Postgres read
//   write  - POST /diagnostics/widgets         one Postgres write + one RabbitMQ publish/consume
//
// It is a baseline, not a pass/fail test: the thresholds below are deliberately trivial (they exist only
// because k6 reports per-tag sub-metrics for a tag combination that has a threshold). Nothing here is a
// target; the numbers are only meaningful relative to later runs on the same machine.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://gateway:8080';
const STEADY_SECONDS = parseInt(__ENV.STEADY_SECONDS || '30', 10);
const LEVELS = (__ENV.VU_LEVELS || '1,10,25,50').split(',').map((v) => parseInt(v, 10));
const PATHS = (__ENV.PERF_PATHS || 'health,read,write').split(',');
const GAP_SECONDS = 5; // quiet gap between levels so one level's tail does not bleed into the next

const rateLimited = new Counter('rate_limited_429');

const scenarios = {};
const thresholds = {};
let start = 0;
for (const path of PATHS) {
  for (const vus of LEVELS) {
    const name = `${path}_vu${vus}`;
    scenarios[name] = {
      executor: 'constant-vus',
      exec: path,
      vus,
      duration: `${STEADY_SECONDS}s`,
      startTime: `${start}s`,
      gracefulStop: '5s',
      tags: { endpoint: path, vus: String(vus) },
    };
    const tag = `{endpoint:${path},vus:${vus}}`;
    thresholds[`http_req_duration${tag}`] = ['max>=0'];
    thresholds[`http_req_failed${tag}`] = ['rate>=0'];
    thresholds[`http_reqs${tag}`] = ['count>=0'];
    thresholds[`rate_limited_429${tag}`] = ['count>=0'];
    start += STEADY_SECONDS + GAP_SECONDS;
  }
}

export const options = {
  scenarios,
  thresholds,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  discardResponseBodies: true,
};

function login(username, password) {
  const res = http.post(`${BASE_URL}/auth/login`, JSON.stringify({ username, password }), {
    headers: { 'Content-Type': 'application/json' },
    responseType: 'text',
  });
  if (res.status !== 200) {
    throw new Error(`login as ${username} failed: HTTP ${res.status}`);
  }
  return res.json('accessToken');
}

// Runs once before the load: one token, reused by every virtual user (login cost is not what is measured).
export function setup() {
  return { token: login('broker', 'Broker-Dev-Pw-123!') };
}

function record(res) {
  if (res.status === 429) {
    rateLimited.add(1);
  }
}

export function health() {
  const res = http.get(`${BASE_URL}/health`);
  record(res);
  check(res, { 'health 200': (r) => r.status === 200 });
}

export function read(data) {
  const res = http.get(`${BASE_URL}/api/reference/countries/IN`, {
    headers: { Authorization: `Bearer ${data.token}` },
  });
  record(res);
  check(res, { 'read 200': (r) => r.status === 200 });
}

export function write() {
  const res = http.post(`${BASE_URL}/diagnostics/widgets?name=perf-baseline`);
  record(res);
  check(res, { 'write 200': (r) => r.status === 200 });
}

const fmt = (n) => (n === undefined || n === null ? '-' : n.toFixed(1));

// Writes the raw k6 summary and a compact table (one row per path x load level) into /results.
export function handleSummary(data) {
  const rows = [];
  for (const path of PATHS) {
    for (const vus of LEVELS) {
      const tag = `{endpoint:${path},vus:${vus}}`;
      const dur = data.metrics[`http_req_duration${tag}`];
      const reqs = data.metrics[`http_reqs${tag}`];
      const failed = data.metrics[`http_req_failed${tag}`];
      const limited = data.metrics[`rate_limited_429${tag}`];
      if (!dur || !reqs) {
        continue;
      }
      rows.push({
        path,
        vus,
        requests: reqs.values.count,
        rps: reqs.values.count / STEADY_SECONDS, // each level runs for STEADY_SECONDS; the built-in rate would divide by the whole test
        avg_ms: dur.values.avg,
        p50_ms: dur.values.med,
        p95_ms: dur.values['p(95)'],
        p99_ms: dur.values['p(99)'],
        max_ms: dur.values.max,
        failed_pct: failed ? failed.values.rate * 100 : 0,
        rate_limited: limited ? limited.values.count : 0,
      });
    }
  }

  const header = 'path    vus  requests    rps   avg_ms  p50_ms  p95_ms  p99_ms  max_ms  failed%  429s';
  const lines = rows.map(
    (r) =>
      `${r.path.padEnd(7)} ${String(r.vus).padStart(3)} ${String(r.requests).padStart(9)} ${fmt(r.rps).padStart(7)} ` +
      `${fmt(r.avg_ms).padStart(8)} ${fmt(r.p50_ms).padStart(7)} ${fmt(r.p95_ms).padStart(7)} ` +
      `${fmt(r.p99_ms).padStart(7)} ${fmt(r.max_ms).padStart(7)} ${fmt(r.failed_pct).padStart(8)} ${String(r.rate_limited).padStart(5)}`,
  );
  const table = [header, ...lines].join('\n') + '\n';

  return {
    '/results/k6-summary.json': JSON.stringify(data, null, 2),
    '/results/baseline-table.txt': table,
    '/results/baseline-rows.json': JSON.stringify(rows, null, 2),
    stdout: '\n' + table,
  };
}
