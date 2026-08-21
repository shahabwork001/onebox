"use client";

import { FormEvent, useState } from "react";
import { login } from "@/lib/api";
import type { Auth } from "@/lib/types";

export function LoginView({ onSignedIn }: { onSignedIn: (auth: Auth) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      onSignedIn(await login(email, password));
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="login">
      <section className="login-form">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            C
          </span>
          <div className="brand-copy">
            <b>CentralChat</b>
            <small>One inbox. Every customer.</small>
          </div>
        </div>

        <h1>Welcome back</h1>
        <p className="login-lede">Sign in to manage your WhatsApp conversations.</p>

        <form onSubmit={submit}>
          <label>
            Email
            <input
              type="email"
              value={email}
              autoComplete="username"
              onChange={event => setEmail(event.target.value)}
              required
            />
          </label>
          <label>
            Password
            <input
              type="password"
              value={password}
              autoComplete="current-password"
              onChange={event => setPassword(event.target.value)}
              required
            />
          </label>

          {error && (
            <div className="form-error" role="alert">
              {error}
            </div>
          )}

          <button type="submit" className="button button-primary button-block" disabled={busy}>
            {busy ? "Signing in…" : "Sign in"}
          </button>
        </form>
      </section>

      <aside className="login-art">
        <div className="login-art-mark" aria-hidden="true">
          ✦
        </div>
        <blockquote>Bring every customer conversation into one calm, focused workspace.</blockquote>
        <p>Secure assignment · Real-time delivery · Complete history</p>
      </aside>
    </main>
  );
}
