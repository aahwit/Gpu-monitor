import { validDashboardSession } from "@/lib/auth";
import { getFleetSummary } from "@/lib/storage";

export async function GET(request: Request) {
  const cookie = request.headers.get("cookie")?.match(/(?:^|;\s*)gpu_dashboard=([^;]+)/)?.[1];
  if (!validDashboardSession(cookie)) return Response.json({ error: "Unauthorized" }, { status: 401 });
  try {
    return Response.json(await getFleetSummary());
  } catch (error) {
    console.error("fleet query failed", error);
    return Response.json({ error: "Unable to load fleet" }, { status: 500 });
  }
}
