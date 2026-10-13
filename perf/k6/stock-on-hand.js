// Read path: GET /api/v1/stock/on-hand for one item at one job site.
//
// The endpoint reads dbo.vw_StockOnHand, the query measured in
// docs/performance.md. The view sums the movement ledger directly, so setup
// only has to receive stock against a posted order for there to be a row.
//
//   docker run --rm -i -e BASE_URL=http://host.docker.internal:8080 grafana/k6 run - < perf/k6/stock-on-hand.js

import http from 'k6/http';
import { check, fail } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const JSON_HEADERS = { 'Content-Type': 'application/json' };
const RATE = Number(__ENV.RATE || 50);

export const options = {
  scenarios: {
    read_on_hand: {
      executor: 'constant-arrival-rate',
      rate: RATE,
      timeUnit: '1s',
      duration: '1m',
      preAllocatedVUs: 20,
      maxVUs: 50,
    },
  },
  // Targets for a pass, not measurements.
  thresholds: {
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    'http_req_duration{name:stock_on_hand}': ['p(95)<200', 'p(99)<400'],
    dropped_iterations: ['count<1'],
  },
};

function postJson(path, body, headers = {}) {
  return http.post(`${BASE_URL}${path}`, body === null ? null : JSON.stringify(body), {
    headers: Object.assign({}, JSON_HEADERS, headers),
    tags: { name: 'setup' },
  });
}

function expect(res, statuses, what) {
  if (![].concat(statuses).includes(res.status)) {
    fail(`${what} returned ${res.status}: ${res.body}`);
  }
  return res.json();
}

export function setup() {
  const run = Date.now().toString(36).toUpperCase();
  const supplierCode = `K6S${run}`;
  const jobSiteCode = `K6J${run}`;
  const itemSku = `K6-VALVE-${run}`;

  expect(postJson('/api/v1/suppliers', { code: supplierCode, name: `k6 supplier ${run}` }), 201, 'supplier');
  expect(
    postJson('/api/v1/items', {
      sku: itemSku,
      description: 'Brass ball valve 20 mm, k6 load test',
      unitOfMeasure: 'ea',
      unitCost: 18.4,
      reorderPoint: 5,
    }),
    201,
    'item',
  );
  expect(postJson('/api/v1/job-sites', { code: jobSiteCode, name: `k6 job site ${run}` }), 201, 'job site');

  const order = expect(postJson('/api/v1/purchase-orders', { supplierCode, jobSiteCode }), 201, 'order');
  expect(postJson(`/api/v1/purchase-orders/${order.id}/lines`, { itemSku, quantityOrdered: 100 }), 201, 'line');
  expect(postJson(`/api/v1/purchase-orders/${order.id}/post`, null, { 'Idempotency-Key': `k6-setup-${run}` }), 200, 'post');
  expect(postJson(`/api/v1/purchase-orders/${order.id}/receipts`, { lines: [{ lineNumber: 1, quantity: 100 }] }), [200, 201], 'receipt');

  const onHand = http.get(`${BASE_URL}/api/v1/stock/on-hand?itemSku=${itemSku}&jobSiteCode=${jobSiteCode}`, {
    tags: { name: 'setup' },
  });
  if (expect(onHand, 200, 'stock on hand').length !== 1) {
    fail('received stock is not on hand');
  }

  return { itemSku, jobSiteCode };
}

export default function (data) {
  const res = http.get(`${BASE_URL}/api/v1/stock/on-hand?itemSku=${data.itemSku}&jobSiteCode=${data.jobSiteCode}`, {
    tags: { name: 'stock_on_hand' },
  });

  check(res, {
    'on hand returns 200': (r) => r.status === 200,
    'one row for the item and site': (r) => r.status === 200 && r.json().length === 1,
    'quantity is the received amount': (r) => r.status === 200 && r.json('0.quantity') === 100,
  });
}
