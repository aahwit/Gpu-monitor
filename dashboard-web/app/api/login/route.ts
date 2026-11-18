import { cookies } from "next/headers";
import { validDashboardKey } from "@/lib/auth";

export async function POST(request: Request) {
  const form = await request.formData();
  const key = String(form.get("key") ?? "");
  if (!validDashboardKey(key)) return Response.redirect(new URL("/login?error=1", request.url), 303);
  const jar = await cookies();
  jar.set("gpu_dashboard", process.env.DASHBOARD_SESSION_TOKEN ?? "", {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "strict",
    maxAge: 60 * 60 * 12,
    path: "/",
  });
  return Response.redirect(new URL("/", request.url), 303);
}
