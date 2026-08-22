"use client";

import { useCallback, useEffect, useRef, useState } from "react";

const STORAGE_KEY = "onebox.alerts";

export type AlertPreferences = { desktop: boolean; sound: boolean };

const DEFAULTS: AlertPreferences = { desktop: true, sound: true };

function readPreferences(): AlertPreferences {
  if (typeof window === "undefined") return DEFAULTS;
  try {
    return { ...DEFAULTS, ...JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? "{}") };
  } catch {
    return DEFAULTS;
  }
}

/**
 * Alerts are presentation of events the client already has, so nothing is fetched or queued here.
 *
 * The rule throughout is that an agent is only told about something they cannot already see: a tab in
 * the background, or a conversation other than the one open in front of them. Notifying someone about
 * the message they are looking at is the fastest way to have them turn notifications off.
 */
export function useNotifications() {
  const [preferences, setPreferences] = useState<AlertPreferences>(DEFAULTS);
  const [permission, setPermission] = useState<NotificationPermission>("default");
  const audioContext = useRef<AudioContext | null>(null);

  useEffect(() => {
    setPreferences(readPreferences());
    if (typeof Notification !== "undefined") setPermission(Notification.permission);
  }, []);

  const update = useCallback((next: Partial<AlertPreferences>) => {
    setPreferences(current => {
      const merged = { ...current, ...next };
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(merged));
      return merged;
    });
  }, []);

  /** Browsers only grant permission from a real gesture, so this is never called on load. */
  const requestPermission = useCallback(async () => {
    if (typeof Notification === "undefined") return "denied" as NotificationPermission;
    const result = await Notification.requestPermission();
    setPermission(result);
    return result;
  }, []);

  // Synthesised rather than shipped as a file: no asset to load, and nothing to fail on a slow network.
  const playChime = useCallback(() => {
    try {
      audioContext.current ??= new AudioContext();
      const context = audioContext.current;
      if (context.state === "suspended") void context.resume();

      const oscillator = context.createOscillator();
      const gain = context.createGain();
      oscillator.type = "sine";
      oscillator.frequency.setValueAtTime(880, context.currentTime);
      oscillator.frequency.setValueAtTime(1170, context.currentTime + 0.09);
      gain.gain.setValueAtTime(0.0001, context.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.12, context.currentTime + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.28);

      oscillator.connect(gain).connect(context.destination);
      oscillator.start();
      oscillator.stop(context.currentTime + 0.3);
    } catch {
      // An alert that cannot make a sound is not a reason to interrupt anything else.
    }
  }, []);

  const notify = useCallback(
    ({ title, body, tag, onClick }: { title: string; body: string; tag: string; onClick?: () => void }) => {
      if (preferences.sound) playChime();
      if (!preferences.desktop || typeof Notification === "undefined" || Notification.permission !== "granted") return;

      // The tag collapses repeats, so ten arrivals leave one notification rather than ten.
      const notification = new Notification(title, { body, tag, icon: "/favicon.ico" });
      notification.onclick = () => {
        window.focus();
        onClick?.();
        notification.close();
      };
    },
    [preferences.desktop, preferences.sound, playChime],
  );

  return { preferences, update, permission, requestPermission, notify };
}

/**
 * Puts the count in the tab title, which is the only alert an agent sees when the window is behind
 * something else and notifications are refused.
 */
export function useTitleBadge(count: number, base = "Onebox") {
  useEffect(() => {
    document.title = count > 0 ? `(${count}) ${base}` : base;
    return () => {
      document.title = base;
    };
  }, [count, base]);
}
