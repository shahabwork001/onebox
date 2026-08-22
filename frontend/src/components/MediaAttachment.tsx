"use client";

import { useEffect, useState } from "react";
import { API_BASE } from "@/lib/api";
import type { Message } from "@/lib/types";
import { IconDownload } from "./icons";

const kindOf = (mime: string | null) => {
  const type = (mime ?? "").split("/")[0];
  if (type === "image") return "image";
  if (type === "video") return "video";
  if (type === "audio") return "audio";
  return "file";
};

const formatBytes = (bytes: number | null) => {
  if (!bytes) return "";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

/**
 * Media is fetched as a blob rather than pointed at with a plain src, because the endpoint is
 * authorised with the caller's bearer token and an img tag cannot send one. The object URL is
 * revoked on unmount so scrolling a long transcript does not leak them.
 */
export function MediaAttachment({ message, token }: { message: Message; token: string }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!message.mediaReady) return;
    let objectUrl: string | null = null;
    let cancelled = false;

    fetch(`${API_BASE}/api/media/${message.id}`, { headers: { Authorization: `Bearer ${token}` } })
      .then(response => (response.ok ? response.blob() : Promise.reject(new Error(String(response.status)))))
      .then(blob => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => !cancelled && setFailed(true));

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [message.id, message.mediaReady, token]);

  const kind = kindOf(message.mimeType);
  const label = `${message.type}${message.mediaSizeBytes ? ` · ${formatBytes(message.mediaSizeBytes)}` : ""}`;

  // Both directions store the binary after recording the message, so this is a normal transient state:
  // inbound is still being fetched from the provider, outbound is still on its way up.
  if (!message.mediaReady)
    return (
      <span className="media-pending">
        {message.direction === "Outbound" ? "Uploading" : "Downloading"} {message.type.toLowerCase()}…
      </span>
    );
  if (failed) return <span className="media-failed">{message.type} unavailable</span>;
  if (!url) return <span className="media-pending">Loading…</span>;

  if (kind === "image")
    return (
      <a href={url} target="_blank" rel="noreferrer" className="media-image">
        <img src={url} alt={message.text ?? "Attachment"} loading="lazy" />
      </a>
    );

  if (kind === "video") return <video className="media-player" src={url} controls preload="metadata" />;
  if (kind === "audio") return <audio className="media-player" src={url} controls preload="metadata" />;

  return (
    <a className="media-file" href={url} download={`${message.id}`}>
      <span className="media-file-icon" aria-hidden="true">
        <IconDownload size={16} />
      </span>
      <span className="media-file-copy">
        <b>{message.type}</b>
        <small>{label}</small>
      </span>
    </a>
  );
}
