import { createHash, timingSafeEqual } from "node:crypto";

function equalSecret(actual: string | null | undefined, expected: string | undefined) {
  if (!actual || !expected) return false;
  const left = createHash("sha256").update(actual).digest();
  const right = createHash("sha256").update(expected).digest();
  return timingSafeEqual(left, right);
}

export function validIngestRequest(request: Request) {
  const authorization = request.headers.get("authorization");
  const token = authorization?.startsWith("Bearer ") ? authorization.slice(7) : request.headers.get("x-gpu-monitor-key");
  return equalSecret(token, process.env.INGEST_API_KEY);
}

export function validDashboardKey(key: string | null | undefined) {
  return equalSecret(key, process.env.DASHBOARD_ACCESS_KEY);
}

export function validDashboardSession(value: string | null | undefined) {
  return equalSecret(value, process.env.DASHBOARD_SESSION_TOKEN);
}
