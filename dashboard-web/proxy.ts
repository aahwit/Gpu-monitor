import { NextResponse, type NextRequest } from "next/server";
import { validDashboardSession } from "@/lib/auth";

export function proxy(request: NextRequest) {
  const session = request.cookies.get("gpu_dashboard")?.value;
  if (!validDashboardSession(session)) return NextResponse.redirect(new URL("/login", request.url));
  return NextResponse.next();
}

export const config = {
  matcher: ["/", "/gpu/:path*", "/api/fleet/:path*"],
};
