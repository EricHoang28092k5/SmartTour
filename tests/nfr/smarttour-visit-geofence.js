/**
 * Visit kiểu Geofence (visitType=0) với tọa độ giả lập trong VN — gần với Geofence Simulator.
 * k6 run -e BASE_URL=https://localhost:7xxx -e POI_ID=1 tests/nfr/smarttour-visit-geofence.js
 */
import { check, sleep } from 'k6';
import {
  checkAccepted,
  defaultThresholds,
  getBaseUrl,
  getJsonHeaders,
  postLogVisit,
} from './k6-common.js';

export function setup() {
  const id = Number(__ENV.POI_ID || 0);
  if (!id || id < 1) {
    throw new Error('Thiếu POI_ID. Ví dụ: -e POI_ID=1');
  }
  return { poiId: id };
}

export const options = {
  vus: Number(__ENV.VUS || 8),
  duration: __ENV.DURATION || '45s',
  thresholds: defaultThresholds(),
};

const base = getBaseUrl();
const headers = getJsonHeaders();

export default function (data) {
  const lat = 10.5 + Math.random() * 2.2;
  const lng = 105.5 + Math.random() * 3.2;
  const res = postLogVisit(base, headers, {
    poiId: data.poiId,
    userId: `SIM-K6-GF-${__VU}-${__ITER}`,
    lat,
    lng,
    visitType: 0,
  });
  checkAccepted(res, 'geofence visit 202');
  sleep(0.2);
}
