import http from 'k6/http';
import { check, sleep } from 'k6';
import { randomSeed, randomItem } from 'k6/jslib/utility.js';

randomSeed(1234);

export const options = {
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.02'],
  },
  scenarios: {
    steady_mix: {
      executor: 'constant-arrival-rate',
      rate: 30,
      timeUnit: '1s',
      duration: '6m',
      preAllocatedVUs: 50,
      maxVUs: 100,
    },
  },
};

const BASE_URL = __ENV.QMS_BASE_URL ?? 'http://localhost:5080';
const API_KEY = __ENV.QMS_API_KEY ?? 'replace_me';
const ENDPOINT = __ENV.QMS_PROJECT ?? 'projectA';

const operations = ['search', 'upsert', 'patch'];

export default function () {
  const op = randomItem(operations);
  let res;

  if (op === 'search') {
    res = http.post(`${BASE_URL}/mcp/${ENDPOINT}/searchEntries`, JSON.stringify({ text: 'release notes', includeShared: true }), {
      headers: headers(),
    });
  } else if (op === 'upsert') {
    const id = `${ENDPOINT}:perf-${__ITER}-${Date.now()}`;
    res = http.post(`${BASE_URL}/mcp/${ENDPOINT}/entries`, JSON.stringify({
      id,
      project: ENDPOINT,
      kind: 'note',
      title: 'Performance test entry',
      body: { text: 'Auto-generated load-test entry' },
      tags: ['load-test'],
    }), {
      headers: headers(),
    });
  } else {
    const id = `${ENDPOINT}:perf-${Math.floor(Math.random() * __ITER || 1)}`;
    res = http.patch(`${BASE_URL}/mcp/${ENDPOINT}/entries/${encodeURIComponent(id)}`, JSON.stringify({
      confidence: Math.random(),
      tags: ['load-test', 'patched'],
    }), {
      headers: headers(),
    });
  }

  check(res, {
    'status < 400': (r) => r.status && r.status < 400,
  });

  sleep(0.5);
}

function headers() {
  return {
    'Content-Type': 'application/json',
    'X-Api-Key': API_KEY,
  };
}
