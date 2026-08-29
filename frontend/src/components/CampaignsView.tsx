"use client";

import { FormEvent, useMemo, useState } from "react";
import type { Campaign, CampaignAudience, MessageTemplate } from "@/lib/types";
import { renderTemplate } from "@/lib/types";
import { formatListTimestamp } from "@/lib/format";
import { Spinner } from "./Primitives";
import { IconRefresh } from "./icons";

export type CampaignActions = {
  onCreate: (input: { name: string; templateName: string; templateLanguage: string; variables: string[] }) => Promise<void>;
  onStart: (campaign: Campaign) => Promise<void>;
  onSetPaused: (campaign: Campaign, paused: boolean) => Promise<void>;
};

/** A broadcast that has left is measured by what reached people, not by what was queued. */
function Progress({ campaign }: { campaign: Campaign }) {
  const total = Math.max(campaign.totalRecipients, 1);
  const share = (value: number) => `${Math.round((value / total) * 100)}%`;

  return (
    <div className="progress">
      <div className="progress-bar" title={`${campaign.sent} sent of ${campaign.totalRecipients}`}>
        <span className="progress-read" style={{ width: share(campaign.read) }} />
        <span className="progress-delivered" style={{ width: share(campaign.delivered - campaign.read) }} />
        <span className="progress-sent" style={{ width: share(campaign.sent - campaign.delivered) }} />
        <span className="progress-failed" style={{ width: share(campaign.failed) }} />
      </div>
      <span className="progress-legend">
        {campaign.sent}/{campaign.totalRecipients} sent
        {campaign.delivered > 0 && ` · ${campaign.delivered} delivered`}
        {campaign.read > 0 && ` · ${campaign.read} read`}
        {campaign.failed > 0 && ` · ${campaign.failed} failed`}
        {campaign.skipped > 0 && ` · ${campaign.skipped} skipped`}
      </span>
    </div>
  );
}

export function CampaignsView({
  campaigns,
  templates,
  audience,
  loading,
  busyId,
  templateError,
  actions,
  onRefresh,
}: {
  campaigns: Campaign[];
  templates: MessageTemplate[];
  audience: CampaignAudience | null;
  loading: boolean;
  busyId: string | null;
  templateError: string | null;
  actions: CampaignActions;
  onRefresh: () => void;
}) {
  const [composing, setComposing] = useState(false);
  const [name, setName] = useState("");
  const [selected, setSelected] = useState("");
  const [variables, setVariables] = useState<string[]>([]);

  const template = useMemo(() => templates.find(t => `${t.name}|${t.language}` === selected), [templates, selected]);
  const usable = templates.filter(t => t.usable);

  const choose = (key: string) => {
    setSelected(key);
    const next = templates.find(t => `${t.name}|${t.language}` === key);
    setVariables(Array.from({ length: next?.variableCount ?? 0 }, () => ""));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!template) return;
    await actions.onCreate({
      name,
      templateName: template.name,
      templateLanguage: template.language,
      variables,
    });
    setName("");
    setSelected("");
    setVariables([]);
    setComposing(false);
  };

  return (
    <section className="screen">
      <header className="panel-header">
        <div>
          <h1>Campaigns</h1>
          <p>
            {audience
              ? `${audience.eligible} of ${audience.contacts} contacts can be reached`
              : "Broadcast an approved template"}
          </p>
        </div>
        <button type="button" className="icon-button" onClick={onRefresh} title="Refresh" aria-label="Refresh">
          <IconRefresh size={17} />
        </button>
      </header>

      <div className="screen-body">
        {/* Marketing is the paid category and the one that gets numbers blocked, so this is stated
            plainly rather than buried in documentation nobody reads. */}
        <div className="notice notice-warn">
          Marketing templates are charged per message and may only go to contacts who opted in. Anyone who
          replies STOP is excluded automatically and permanently.
        </div>

        {templateError && (
          <div className="notice notice-danger" role="alert">
            {templateError}
          </div>
        )}

        {audience && (
          <div className="metric-grid">
            <div className="metric">
              <span className="metric-label">Eligible</span>
              <strong className="metric-value">{audience.eligible}</strong>
              <span className="metric-hint">Will receive this broadcast</span>
            </div>
            <div className={audience.optedOut > 0 ? "metric metric-warn" : "metric"}>
              <span className="metric-label">Opted out</span>
              <strong className="metric-value">{audience.optedOut}</strong>
              <span className="metric-hint">Excluded permanently</span>
            </div>
            <div className="metric">
              <span className="metric-label">Inactive</span>
              <strong className="metric-value">{audience.inactive}</strong>
              <span className="metric-hint">Blocked or archived</span>
            </div>
          </div>
        )}

        <div className="section-head">
          <h2 className="section-title">Broadcasts</h2>
          <button
            type="button"
            className="button button-primary"
            disabled={usable.length === 0}
            title={usable.length === 0 ? "No approved templates are available on your business account" : undefined}
            onClick={() => setComposing(value => !value)}
          >
            {composing ? "Cancel" : "New broadcast"}
          </button>
        </div>

        {composing && (
          <form className="panel-form campaign-form" onSubmit={submit}>
            <label className="span-2">
              Name
              <input value={name} onChange={e => setName(e.target.value)} placeholder="Spring promotion" required />
            </label>

            <label className="span-2">
              Template
              <select value={selected} onChange={e => choose(e.target.value)} required>
                <option value="">Choose an approved template…</option>
                {usable.map(t => (
                  <option key={`${t.name}|${t.language}`} value={`${t.name}|${t.language}`}>
                    {t.name} · {t.category.toLowerCase()} · {t.language}
                  </option>
                ))}
              </select>
            </label>

            {variables.map((value, index) => (
              <label key={index}>
                {`Value for {{${index + 1}}}`}
                <input
                  value={value}
                  onChange={e => setVariables(current => current.map((v, i) => (i === index ? e.target.value : v)))}
                  required
                />
              </label>
            ))}

            {template && (
              <div className="span-2">
                <span className="preview-label">Preview — what the customer receives</span>
                <div className="template-preview">{renderTemplate(template.body, variables)}</div>
              </div>
            )}

            <button type="submit" className="button button-primary" disabled={!template}>
              Create as draft
            </button>
          </form>
        )}

        {loading && campaigns.length === 0 && <Spinner label="Loading campaigns…" />}

        {!loading && campaigns.length === 0 && (
          <p className="list-empty">No broadcasts yet. Approved templates come from your WhatsApp Business account.</p>
        )}

        {campaigns.length > 0 && (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Campaign</th>
                  <th>Status</th>
                  <th>Progress</th>
                  <th className="num">Action</th>
                </tr>
              </thead>
              <tbody>
                {campaigns.map(campaign => (
                  <tr key={campaign.id}>
                    <td>
                      <span className="cell-copy">
                        <b>{campaign.name}</b>
                        <small>
                          {campaign.templateName} · {campaign.templateLanguage}
                          {campaign.startedAt && ` · started ${formatListTimestamp(campaign.startedAt)}`}
                        </small>
                      </span>
                    </td>
                    <td>
                      <span className={`badge badge-${campaign.status.toLowerCase()}`}>{campaign.status}</span>
                      {campaign.failureReason && <small className="cell-error">{campaign.failureReason}</small>}
                    </td>
                    <td>
                      {campaign.status === "Draft" ? (
                        <span className="progress-legend">
                          Not sent yet · {audience?.eligible ?? 0} will receive it
                        </span>
                      ) : (
                        <Progress campaign={campaign} />
                      )}
                    </td>
                    <td className="num">
                      <span className="row-actions">
                        {campaign.status === "Draft" && (
                          <button
                            type="button"
                            className="button button-primary"
                            disabled={busyId === campaign.id}
                            onClick={() => actions.onStart(campaign)}
                          >
                            {busyId === campaign.id ? "Sending…" : "Send now"}
                          </button>
                        )}
                        {campaign.status === "Sending" && (
                          <button
                            type="button"
                            className="button button-ghost"
                            disabled={busyId === campaign.id}
                            onClick={() => actions.onSetPaused(campaign, true)}
                          >
                            Pause
                          </button>
                        )}
                        {campaign.status === "Paused" && (
                          <button
                            type="button"
                            className="button button-primary"
                            disabled={busyId === campaign.id}
                            onClick={() => actions.onSetPaused(campaign, false)}
                          >
                            Resume
                          </button>
                        )}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}
