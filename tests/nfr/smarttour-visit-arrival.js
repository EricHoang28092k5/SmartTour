/**
 * Tải visit theo tốc độ cố định (constant-arrival-rate) — tránh vượt DeviceTokenPolicy (60 req/10s/IP).
 * Bắt buộc: POI_ID. Mặc định ~45 lượt / 10s trong 90s.
 *
 * k6 run -e BASE_URL=https://localhost:7xxx -e POI_ID=1 tests/nfr/smarttour-visit-arrival.js
 * k6 run -e BASE_URL=... -e POI_ID=1 -e ARRIVAL_PER_10S=55 -e DURATION=2m tests/nfr/smarttour-visit-arrival.js
 */
import {
  checkAccepted,
  defaultThresholds,
  getBaseUrl,
  getJsonHeaders,
  postLogVisit,
} from './k6-common.js';

const arrival = Math.min(58, Math.max(1, Number(__ENV.ARRIVAL_PER_10S || 45)));
const duration = __ENV.DURATION || '90s';

export function setup() {
  const id = Number(__ENV.POI_ID || 0);
  if (!id || id < 1) {
    throw new Error('Thiếu POI_ID hợp lệ. Ví dụ: -e POI_ID=1');
  }
  return { poiId: id };
}

export const options = {
  scenarios: {
    visit_arrival: {
      executor: 'constant-arrival-rate',
      rate: arrival,
      timeUnit: '10s',
      duration,
      preAllocatedVUs: Math.min(80, arrival * 3),
      maxVUs: 200,
    },
  },
  thresholds: defaultThresholds(),
};

const base = getBaseUrl();
const headers = getJsonHeaders();

export default function (data) {
  const body = {
    poiId: data.poiId,
    userId: `SIM-K6-${__VU}-${__ITER}-${Date.now()}`,
    lat: 0,
    lng: 0,
    visitType: 1,
  };
  const res = postLogVisit(base, headers, body);
  checkAccepted(res, 'visit 202');
}
