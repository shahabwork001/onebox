"use client";

import { FormEvent, useState } from "react";
import { ROLES, type Agent } from "@/lib/types";
import { Avatar, Spinner } from "./Primitives";
import { IconRefresh } from "./icons";

export type TeamActions = {
  onCreate: (input: { email: string; displayName: string; password: string; role: string }) => Promise<void>;
  onChangeRole: (agent: Agent, role: string) => Promise<void>;
  onSetActive: (agent: Agent, isActive: boolean) => Promise<void>;
  onResetPassword: (agent: Agent, password: string) => Promise<void>;
  onChangeOwnPassword: (currentPassword: string, newPassword: string) => Promise<void>;
};

export function TeamView({
  agents,
  loading,
  busyId,
  currentUserId,
  actions,
  onRefresh,
}: {
  agents: Agent[];
  loading: boolean;
  busyId: string | null;
  currentUserId: string;
  actions: TeamActions;
  onRefresh: () => void;
}) {
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ email: "", displayName: "", password: "", role: "Agent" });
  const [ownPassword, setOwnPassword] = useState({ current: "", next: "" });
  const [notice, setNotice] = useState("");

  const submitNew = async (event: FormEvent) => {
    event.preventDefault();
    await actions.onCreate(form);
    setForm({ email: "", displayName: "", password: "", role: "Agent" });
    setAdding(false);
  };

  const submitOwnPassword = async (event: FormEvent) => {
    event.preventDefault();
    await actions.onChangeOwnPassword(ownPassword.current, ownPassword.next);
    setOwnPassword({ current: "", next: "" });
    setNotice("Your password has been changed.");
  };

  const resetFor = async (agent: Agent) => {
    const password = window.prompt(`Set a new password for ${agent.displayName}`);
    if (!password) return;
    await actions.onResetPassword(agent, password);
    setNotice(`Password updated for ${agent.displayName}. Their existing sessions were signed out.`);
  };

  return (
    <section className="screen">
      <header className="panel-header">
        <div>
          <h1>Team</h1>
          <p>
            {agents.filter(a => a.isActive).length} active · {agents.length} total
          </p>
        </div>
        <button type="button" className="icon-button" onClick={onRefresh} title="Refresh" aria-label="Refresh">
          <IconRefresh size={17} />
        </button>
      </header>

      <div className="screen-body">
        {notice && (
          <div className="notice" role="status">
            {notice}
          </div>
        )}

        <div className="section-head">
          <h2 className="section-title">Agents</h2>
          <button type="button" className="button button-primary" onClick={() => setAdding(value => !value)}>
            {adding ? "Cancel" : "Add agent"}
          </button>
        </div>

        {adding && (
          <form className="panel-form" onSubmit={submitNew}>
            <label>
              Full name
              <input value={form.displayName} onChange={e => setForm({ ...form, displayName: e.target.value })} required />
            </label>
            <label>
              Email
              <input type="email" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} required />
            </label>
            <label>
              Temporary password
              <input
                type="text"
                value={form.password}
                onChange={e => setForm({ ...form, password: e.target.value })}
                placeholder="At least 10 characters"
                required
              />
            </label>
            <label>
              Role
              <select value={form.role} onChange={e => setForm({ ...form, role: e.target.value })}>
                {ROLES.map(role => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
            </label>
            <button type="submit" className="button button-primary">
              Create account
            </button>
          </form>
        )}

        {loading && agents.length === 0 && <Spinner label="Loading team…" />}

        {agents.length > 0 && (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Agent</th>
                  <th>Role</th>
                  <th>Status</th>
                  <th className="num">Actions</th>
                </tr>
              </thead>
              <tbody>
                {agents.map(agent => {
                  const isSelf = agent.id === currentUserId;
                  return (
                    <tr key={agent.id} className={agent.isActive ? undefined : "row-muted"}>
                      <td>
                        <span className="cell-identity">
                          <Avatar name={agent.displayName} size="sm" />
                          <span className="cell-copy">
                            <b>
                              {agent.displayName}
                              {isSelf && <span className="self-tag">you</span>}
                            </b>
                            <small>{agent.email}</small>
                          </span>
                        </span>
                      </td>
                      <td>
                        <select
                          className="inline-select"
                          value={agent.roles[0] ?? "Agent"}
                          disabled={isSelf || busyId === agent.id}
                          onChange={e => actions.onChangeRole(agent, e.target.value)}
                        >
                          {ROLES.map(role => (
                            <option key={role} value={role}>
                              {role}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <span className={agent.isActive ? "badge badge-open" : "badge"}>
                          {agent.isActive ? "Active" : "Deactivated"}
                        </span>
                      </td>
                      <td className="num">
                        <span className="row-actions">
                          <button
                            type="button"
                            className="button button-ghost"
                            disabled={busyId === agent.id}
                            onClick={() => resetFor(agent)}
                          >
                            Reset password
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            disabled={isSelf || busyId === agent.id}
                            title={isSelf ? "You cannot deactivate your own account" : undefined}
                            onClick={() => actions.onSetActive(agent, !agent.isActive)}
                          >
                            {agent.isActive ? "Deactivate" : "Reactivate"}
                          </button>
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        <h2 className="section-title">Your password</h2>
        <form className="panel-form" onSubmit={submitOwnPassword}>
          <label>
            Current password
            <input
              type="password"
              autoComplete="current-password"
              value={ownPassword.current}
              onChange={e => setOwnPassword({ ...ownPassword, current: e.target.value })}
              required
            />
          </label>
          <label>
            New password
            <input
              type="password"
              autoComplete="new-password"
              value={ownPassword.next}
              onChange={e => setOwnPassword({ ...ownPassword, next: e.target.value })}
              required
            />
          </label>
          <button type="submit" className="button button-ghost">
            Change password
          </button>
        </form>
      </div>
    </section>
  );
}
