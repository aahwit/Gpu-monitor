import { Activity, LockKeyhole } from "lucide-react";

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ error?: string }> }) {
  const { error } = await searchParams;
  return (
    <main className="login-page">
      <section className="login-card">
        <div className="brand-mark"><Activity size={22} /></div>
        <p className="eyebrow">KENRI OPERATIONS</p>
        <h1>GPU Fleet Control</h1>
        <p className="login-copy">Private operations view for GPU health, capacity, and early-warning signals.</p>
        <form action="/api/login" method="post">
          <label htmlFor="key">Dashboard access key</label>
          <div className="input-wrap"><LockKeyhole size={17} /><input id="key" name="key" type="password" autoComplete="current-password" required autoFocus /></div>
          {error ? <p className="form-error">The access key is incorrect.</p> : null}
          <button type="submit">Open dashboard</button>
        </form>
      </section>
    </main>
  );
}
