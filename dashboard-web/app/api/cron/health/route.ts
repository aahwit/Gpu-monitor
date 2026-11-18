import { getFleetSummary } from "@/lib/storage";

export async function GET(request: Request) {
  if (!process.env.CRON_SECRET || request.headers.get("authorization") !== `Bearer ${process.env.CRON_SECRET}`)
    return Response.json({ error: "Unauthorized" }, { status: 401 });
  const { online, offline, heat } = await getFleetSummary();
  return Response.json({ online, offline, danger: heat.danger });
}
