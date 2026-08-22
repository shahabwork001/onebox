#!/usr/bin/env node
/**
 * Answers one question with a number instead of an estimate: at N connected agents and M inbound
 * messages a second, does the workspace keep up, and where does it start to bend?
 *
 * It holds N live hub connections open — the thing that actually costs, because every connection is in
 * the broadcast group and sees every ticket change — while driving webhooks at a fixed rate and
 * sampling the endpoint agents read most. It reports latency percentiles rather than averages, since
 * an average hides exactly the stalls you care about.
 *
 * This writes real conversations. Point it at a local or staging instance, never at production.
 */
const signalR = require("@microsoft/signalr");
const { Agent, setGlobalDispatcher } = require("undici");

// Node's default connection pool is far smaller than the load being generated, so without this the
// tool becomes its own bottleneck and reports client timeouts as server failures.
setGlobalDispatcher(new Agent({ connections: 256, pipelining: 0 }));

const config = {
  baseUrl: process.env.BASE_URL ?? "http://127.0.0.1:8080",
  email: process.env.EMAIL ?? "admin@onebox.local",
  password: process.env.PASSWORD ?? "1w1I_mz49OU_0-oZ",
  agents: Number(process.env.AGENTS ?? 100),
  messagesPerSecond: Number(process.env.RATE ?? 5),
  durationSeconds: Number(process.env.DURATION ?? 60),
  contacts: Number(process.env.CONTACTS ?? 200),
  phoneNumberId: process.env.PHONE_NUMBER_ID ?? "708123456789012",
};

const metrics = {
  eventsReceived: 0,
  webhooksSent: 0,
  webhookErrors: 0,
  listErrors: 0,
  connectFailures: 0,
  disconnects: 0,
  listLatency: [],
  webhookLatency: [],
  // A failure the generator caused is not a failure of the server, and reporting them together would
  // make the whole measurement worthless. They are counted separately and named.
  failures: new Map(),
};

const recordFailure = reason => metrics.failures.set(reason, (metrics.failures.get(reason) ?? 0) + 1);

const percentile = (samples, p) => {
  if (samples.length === 0) return 0;
  const sorted = [...samples].sort((a, b) => a - b);
  return sorted[Math.min(Math.floor((p / 100) * sorted.length), sorted.length - 1)];
};

const summarise = samples =>
  samples.length === 0
    ? "no samples"
    : `p50 ${percentile(samples, 50).toFixed(0)}ms  p95 ${percentile(samples, 95).toFixed(0)}ms  ` +
      `p99 ${percentile(samples, 99).toFixed(0)}ms  max ${Math.max(...samples).toFixed(0)}ms`;

async function login() {
  const response = await fetch(`${config.baseUrl}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: config.email, password: config.password }),
  });
  if (!response.ok) throw new Error(`Login failed (${response.status}). Check EMAIL and PASSWORD.`);
  return (await response.json()).accessToken;
}

/** Every connection joins the broadcast group, so N of these is what one ticket change has to fan out to. */
async function connectAgents(token) {
  const connections = [];
  for (let i = 0; i < config.agents; i++) {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${config.baseUrl}/hubs/communication`, { accessTokenFactory: () => token })
      .configureLogging(signalR.LogLevel.None)
      .build();

    for (const event of ["ticket.upserted", "ticket.removed", "message.received", "message.sent"]) {
      connection.on(event, () => metrics.eventsReceived++);
    }
    connection.onclose(() => metrics.disconnects++);

    try {
      await connection.start();
      connections.push(connection);
    } catch {
      metrics.connectFailures++;
    }
    if ((i + 1) % 25 === 0) process.stdout.write(`  connected ${i + 1}/${config.agents}\n`);
  }
  return connections;
}

async function sendWebhook(index) {
  const waId = `92300${String(1000000 + (index % config.contacts)).slice(-7)}`;
  const body = {
    entry: [{ changes: [{ value: {
      metadata: { phone_number_id: config.phoneNumberId },
      contacts: [{ profile: { name: `Load Contact ${index % config.contacts}` }, wa_id: waId }],
      messages: [{
        id: `wamid.load.${Date.now()}.${index}`,
        from: waId,
        timestamp: String(Math.floor(Date.now() / 1000)),
        type: "text",
        text: { body: `Load message ${index}` },
      }],
    } }] }],
  };

  const started = performance.now();
  try {
    const response = await fetch(`${config.baseUrl}/webhook`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    metrics.webhookLatency.push(performance.now() - started);
    if (response.ok) metrics.webhooksSent++;
    else {
      metrics.webhookErrors++;
      recordFailure(`webhook HTTP ${response.status}`);
    }
  } catch (error) {
    metrics.webhookErrors++;
    recordFailure(`webhook client: ${error.cause?.code ?? error.message}`);
  }
}

/** The endpoint an agent's reconciliation hits; its latency under load is what they would feel. */
async function sampleList(token) {
  const started = performance.now();
  try {
    const response = await fetch(`${config.baseUrl}/api/tickets?scope=unassigned&status=active&pageSize=100`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    metrics.listLatency.push(performance.now() - started);
    if (!response.ok) {
      metrics.listErrors++;
      recordFailure(`list HTTP ${response.status}`);
    }
  } catch (error) {
    metrics.listErrors++;
    recordFailure(`list client: ${error.cause?.code ?? error.message}`);
  }
}

(async () => {
  console.log(`Onebox load test`);
  console.log(`  target      ${config.baseUrl}`);
  console.log(`  agents      ${config.agents} live hub connections`);
  console.log(`  inbound     ${config.messagesPerSecond}/s across ${config.contacts} contacts`);
  console.log(`  duration    ${config.durationSeconds}s\n`);

  const token = await login();
  console.log("connecting agents...");
  const connectStarted = performance.now();
  const connections = await connectAgents(token);
  console.log(`  ${connections.length} connected in ${((performance.now() - connectStarted) / 1000).toFixed(1)}s`);
  if (metrics.connectFailures) console.log(`  ${metrics.connectFailures} failed to connect`);

  console.log("\ndriving traffic...");
  const eventsAtStart = metrics.eventsReceived;
  const started = performance.now();
  let index = 0;

  const traffic = setInterval(() => {
    for (let i = 0; i < config.messagesPerSecond; i++) sendWebhook(index++);
  }, 1000);
  // Sampled independently of the traffic so a slow response cannot throttle the load generator.
  const sampling = setInterval(() => sampleList(token), 1000);

  const progress = setInterval(() => {
    const elapsed = (performance.now() - started) / 1000;
    process.stdout.write(
      `  ${elapsed.toFixed(0)}s  sent ${metrics.webhooksSent}  events ${metrics.eventsReceived}  ` +
      `list p95 ${percentile(metrics.listLatency, 95).toFixed(0)}ms\n`);
  }, 10000);

  await new Promise(resolve => setTimeout(resolve, config.durationSeconds * 1000));
  clearInterval(traffic);
  clearInterval(sampling);
  clearInterval(progress);

  // Events lag the last webhook by a queue hop, so give the consumer a moment to drain.
  console.log("\ndraining...");
  await new Promise(resolve => setTimeout(resolve, 8000));

  const elapsed = (performance.now() - started) / 1000;
  const delivered = metrics.eventsReceived - eventsAtStart;

  console.log(`\n${"=".repeat(62)}`);
  console.log(`RESULTS  ${config.agents} agents, ${config.messagesPerSecond}/s inbound, ${elapsed.toFixed(0)}s`);
  console.log("=".repeat(62));
  console.log(`  webhooks accepted    ${metrics.webhooksSent}  (${metrics.webhookErrors} failed)`);
  console.log(`  webhook latency      ${summarise(metrics.webhookLatency)}`);
  console.log(`  list latency         ${summarise(metrics.listLatency)}  (${metrics.listErrors} failed)`);
  console.log(`  hub events delivered ${delivered}`);
  console.log(`  fan-out per message  ${metrics.webhooksSent ? (delivered / metrics.webhooksSent).toFixed(1) : 0} (expect ~${config.agents})`);
  console.log(`  disconnects          ${metrics.disconnects}`);
  if (metrics.failures.size > 0) {
    console.log(`  failures by cause:`);
    for (const [reason, count] of [...metrics.failures].sort((a, b) => b[1] - a[1])) {
      console.log(`    ${String(count).padStart(5)}  ${reason}`);
    }
  } else {
    console.log(`  failures             none`);
  }
  console.log("=".repeat(62));
  console.log(`\nWhat to look for: webhook p95 should stay flat — it only writes a row and returns.`);
  console.log(`List p95 climbing as agents rise is the signal that reconciliation is the constraint.`);

  await Promise.all(connections.map(c => c.stop().catch(() => undefined)));
  process.exit(0);
})().catch(error => {
  console.error("\nload test failed:", error.message);
  process.exit(1);
});
