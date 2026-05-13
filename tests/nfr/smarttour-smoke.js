/**
 * Smoke: /api/health + (tuỳ chọn) một POST visit nếu đặt POI_ID.
 * k6 run -e BASE_URL=https://localhost:7xxx -e POI_ID=1 tests/nfr/smarttour-smoke.js
 */
import { sleep } from 'k6';
import {
  check2xx,
  checkAccepted,
  defaultThresholds,
  getBaseUrl,
  getHealth,
  getJsonHeaders,
  postLogVisit,
} from './k6-common.js';

export const options = {
  vus: 5,
  duration: '20s',
  thresholds: defaultThresholds(),
};

const base = getBaseUrl();
const headers = getJsonHeaders();
const poiId = Number(__ENV.POI_ID || 0);

export default function () {
  const h = getHealth(base, headers);
  check2xx(h, 'health 2xx');

  if (poiId > 0) {
    const body = {
      poiId,
      userId: `SIM-K6-SMOKE-${__VU}-${__ITER}`,
      lat: 10.7769,
      lng: 106.7008,
      visitType: 1,
    };
    const v = postLogVisit(base, headers, body);
    checkAccepted(v, 'visit 202 Accepted');
  }
  sleep(0.15);
}
