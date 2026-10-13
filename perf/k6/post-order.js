// Write path: create a purchase order, add a line, and post it.
//
// Posting runs dbo.usp_PostPurchaseOrder (order row lock, outbox row, webhook
// delivery row in one transaction), so this is the endpoint whose latency the
// thresholds care about.
//
//   docker run --rm -i -e BASE_URL=http://host.docker.internal:8080 -e RATE=5 -e DURATION=20s grafana/k6 run - < perf/k6/post-order.js

import http from 'k6/http';
import { check, fail } from 'k6';
import exec from 'k6/execution';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const JSON_HEADERS = { 'Content-Type': 'application/json' };

const RATE = Number(__ENV.RATE || 5);
const DURATION = __ENV.DURATION || '1m';

export const options = {
  scenarios: {
    post_orders: {
      executor: 'constant-arrival-rate',
      rate: RATE,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 10,
      maxVUs: 50,
    },
  },
  // Targets, not results: the numbers a run has to meet to pass. Setup
  // requests are excluded from the latency thresholds by their tag.
  thresholds: {
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    'http_req_duration{name:create_order}': ['p(95)<300'],
    'http_req_duration{name:add_line}': ['p(95)<300'],
    'http_req_duration{name:post_order}': ['p(95)<500', 'p(99)<1000'],
  },
};

function postJson(path, body, name) {
  return http.post(`${BASE_URL}${path}`, JSON.stringify(body), {
    headers: JSON_HEADERS,
    tags: { name },
  });
}

function created(res, what) {
  if (!check(res, { [`${what} created`]: (r) => r.status === 201 })) {
    fail(`${what} failed with ${res.status}: ${res.body}`);
  }
  return res.json();
}

// One supplier, item, and job site per run. The run id keeps codes unique
// between runs and inside the 16 character limit for codes.
export function setup() {
  const run = Date.now().toString(36).toUpperCase();

  const supplier = created(
    postJson('/api/v1/suppliers', { code: `K6S${run}`, name: `k6 supplier ${run}` }, 'setup'),
    'supplier',
  );
  const item = created(
    postJson(
      '/api/v1/items',
      {
        sku: `K6-PIPE-${run}`,
        description: 'Copper pipe 15 mm, k6 load test',
        unitOfMeasure: 'm',
        unitCost: 4.85,
        reorderPoint: 0,
      },
      'setup',
    ),
    'item',
  );
  const jobSite = created(
    postJson('/api/v1/job-sites', { code: `K6J${run}`, name: `k6 job site ${run}` }, 'setup'),
    'job site',
  );

  return {
    run,
    supplierCode: supplier.code,
    itemSku: item.sku,
    jobSiteCode: jobSite.code,
  };
}

export default function (data) {
  const order = created(
    postJson('/api/v1/purchase-orders', { supplierCode: data.supplierCode, jobSiteCode: data.jobSiteCode }, 'create_order'),
    'order',
  );

  created(
    postJson(
      `/api/v1/purchase-orders/${order.id}/lines`,
      { itemSku: data.itemSku, quantityOrdered: 10, unitCost: 4.85 },
      'add_line',
    ),
    'line',
  );

  const key = `k6-${data.run}-${exec.vu.idInTest}-${exec.vu.iterationInScenario}`;
  const posted = http.post(`${BASE_URL}/api/v1/purchase-orders/${order.id}/post`, null, {
    headers: { 'Idempotency-Key': key },
    tags: { name: 'post_order' },
  });

  check(posted, {
    'post returns 200': (r) => r.status === 200,
    'order is posted': (r) => r.status === 200 && r.json('status') === 'Posted',
  });
}
