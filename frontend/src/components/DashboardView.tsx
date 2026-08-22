import type { Dashboard } from "@/lib/types";
import { formatDuration } from "@/lib/format";
import { Avatar, Spinner } from "./Primitives";
import { IconRefresh } from "./icons";

function Metric({ label, value, hint, tone }: { label: string; value: string | number; hint?: string; tone?: "warn" | "good" }) {
  return (
    <div className={tone ? `metric metric-${tone}` : "metric"}>
      <span className="metric-label">{label}</span>
      <strong className="metric-value">{value}</strong>
      {hint && <span className="metric-hint">{hint}</span>}
    </div>
  );
}

export function DashboardView({
  data,
  loading,
  displayName,
  canSeeAgents,
  onOpenQueue,
  onRefresh,
}: {
  data: Dashboard | null;
  loading: boolean;
  displayName: string;
  canSeeAgents: boolean;
  onOpenQueue: () => void;
  onRefresh: () => void;
}) {
  return (
    <section className="screen">
      <header className="panel-header">
        <div>
          <h1>Dashboard</h1>
          <p>Welcome back, {displayName}</p>
        </div>
        <button type="button" className="icon-button" onClick={onRefresh} title="Refresh" aria-label="Refresh">
          <IconRefresh size={17} />
        </button>
      </header>

      <div className="screen-body">
        {!data && loading && <Spinner label="Loading metrics…" />}

        {data && (
          <>
            {data.totals.unassigned > 0 && (
              <button type="button" className="queue-callout" onClick={onOpenQueue}>
                <strong>
                  {data.totals.unassigned} conversation{data.totals.unassigned === 1 ? "" : "s"} waiting to be claimed
                </strong>
                <span className="callout-cta">Open the queue</span>
              </button>
            )}

            <h2 className="section-title">Conversations</h2>
            <div className="metric-grid">
              <Metric label="Total conversations" value={data.totals.conversations} />
              <Metric label="Contacts" value={data.totals.contacts} />
              <Metric
                label="Waiting to claim"
                value={data.totals.unassigned}
                tone={data.totals.unassigned > 0 ? "warn" : undefined}
              />
              <Metric label="Open" value={data.totals.open} />
              <Metric label="Resolved" value={data.totals.resolved} tone="good" />
              <Metric label="Closed" value={data.totals.closed} />
            </div>

            <h2 className="section-title">Messages</h2>
            <div className="metric-grid">
              <Metric label="Received" value={data.totals.inboundMessages} />
              <Metric label="Sent" value={data.totals.outboundMessages} />
              <Metric
                label="Avg first response"
                value={formatDuration(data.totals.avgFirstResponseSeconds)}
                hint="Customer's first message to first reply"
              />
            </div>

            {canSeeAgents && (
              <>
                <h2 className="section-title">Agents</h2>
                <div className="table-wrap">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Agent</th>
                        <th className="num">Claimed</th>
                        <th className="num">Open</th>
                        <th className="num">Resolved</th>
                        <th className="num">Avg first response</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.agents.map(agent => (
                        <tr key={agent.agentId}>
                          <td>
                            <span className="cell-identity">
                              <Avatar name={agent.displayName} size="sm" />
                              <span className="cell-copy">
                                <b>{agent.displayName}</b>
                                <small>{agent.email}</small>
                              </span>
                            </span>
                          </td>
                          <td className="num">{agent.claimed}</td>
                          <td className="num">{agent.open}</td>
                          <td className="num">{agent.resolved}</td>
                          <td className="num">{formatDuration(agent.avgFirstResponseSeconds)}</td>
                        </tr>
                      ))}
                      {data.agents.length === 0 && (
                        <tr>
                          <td colSpan={5} className="table-empty">
                            No active agents yet.
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </>
        )}
      </div>
    </section>
  );
}
