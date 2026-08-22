import type { Auth } from "./types";

const STORAGE_KEY = "onebox.auth";

export function readSession(): Auth | null {
  if (typeof window === "undefined") return null;
  const saved = window.localStorage.getItem(STORAGE_KEY);
  if (!saved) return null;

  try {
    const auth = JSON.parse(saved) as Auth;
    // A token past its expiry is worse than no token: every call would 401 behind a logged-in shell.
    if (auth?.expiresAt && new Date(auth.expiresAt).getTime() <= Date.now()) {
      window.localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    return auth?.accessToken ? auth : null;
  } catch {
    window.localStorage.removeItem(STORAGE_KEY);
    return null;
  }
}

export function writeSession(auth: Auth) {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(auth));
}

export function clearSession() {
  window.localStorage.removeItem(STORAGE_KEY);
}
