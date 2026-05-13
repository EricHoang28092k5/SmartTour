/**
 * Shared helpers for SmartTour k6 NFR scripts (API trực tiếp — không cookie CMS).
 */
import { check } from 'k6';
import http from 'k6/http';

export function getBaseUrl() {
  return (__ENV.BASE_URL || __ENV.K6_BASE_URL || 'https://localhost:7xxx').replace(/\/$/, '');
}

export function getJsonHeaders() {
  return {
    'Content-Type': 'application/json',
    Accept: 'application/json',
    'ngrok-skip-browser-warning': 'true',
  };
}

export function defaultThresholds() {
  return {
    http_req_failed: ['rate<0.02'],
    'http_req_duration{expected_response:true}': ['p(95)<800'],
  };
}

export function check2xx(res, name) {
  return check(res, { [name]: (r) => r.status >= 200 && r.status < 300 });
}

export function checkAccepted(res, name) {
  return check(res, { [name]: (r) => r.status === 202 });
}

/** POST /api/analytics/visit (AllowAnonymous, DeviceTokenPolicy ~60/10s/IP). */
export function postLogVisit(base, headers, payload) {
  return http.post(`${base}/api/analytics/visit`, JSON.stringify(payload), { headers });
}

export function getHealth(base, headers) {
  return http.get(`${base}/api/health`, { headers });
}
