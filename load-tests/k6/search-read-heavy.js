import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  thresholds: {
    http_req_duration: ['p(95)<300'],
    http_req_failed: ['rate<0.01'],
  },
  scenarios: {
    search_burst: {
      executor: 'ramping-arrival-rate',
      startRate: 5,
      timeUnit: '1s',
      preAllocatedVUs: 20,
      maxVUs: 100,
      stages: [
        { target: 20, duration: '2m' },
        { target: 60, duration: '3m' },
        { target: 0, duration: '1m' },
      ],
    },
  },
};

const BASE_URL = __ENV.QMS_BASE_URL ?? 'http://localhost:5080';
const API_KEY = __ENV.QMS_API_KEY ?? 'replace_me';
const ENDPOINT = __ENV.QMS_PROJECT ?? 'projectA';

export default function () {
  const payload = JSON.stringify({
    text: 'search index rebuild',
    includeShared: true,
    maxResults: 20,
  });

  const res = http.post(`${BASE_URL}/mcp/${ENDPOINT}/searchEntries`, payload, {
    headers: {
      'Content-Type': 'application/json',
      'X-Api-Key': API_KEY,
    },
  });

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(1);
}
