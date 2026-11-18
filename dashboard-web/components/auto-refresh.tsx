"use client";

import { useEffect, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { RefreshCw } from "lucide-react";

export function AutoRefresh() {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [seconds, setSeconds] = useState(15);

  useEffect(() => {
    const timer = window.setInterval(() => {
      setSeconds((current) => {
        if (current <= 1) {
          startTransition(() => router.refresh());
          return 15;
        }
        return current - 1;
      });
    }, 1000);
    return () => window.clearInterval(timer);
  }, [router]);

  return (
    <button className="refresh" type="button" onClick={() => startTransition(() => router.refresh())} aria-label="Refresh dashboard">
      <RefreshCw size={15} className={pending ? "spinning" : ""} />
      {pending ? "Refreshing" : `Refresh · ${seconds}s`}
    </button>
  );
}
