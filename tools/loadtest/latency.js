// Measures what an agent actually waits: from a customer's message arriving at the webhook to it
// appearing on a connected agent's screen. That is the whole path — HTTP, outbox, queue, consumer, hub.
const signalR = require("@microsoft/signalr");

const B = process.env.BASE_URL ?? "http://127.0.0.1:8089";
const ROUNDS = Number(process.env.ROUNDS ?? 12);

const percentile = (xs, p) => {
  const s = [...xs].sort((a, b) => a - b);
  return s[Math.min(Math.floor((p / 100) * s.length), s.length - 1)];
};

(async () => {
  const login = await (await fetch(`${B}/api/auth/login`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: "admin@onebox.local", password: "1w1I_mz49OU_0-oZ" }),
  })).json();

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${B}/hubs/communication`, { accessTokenFactory: () => login.accessToken })
    .configureLogging(signalR.LogLevel.None).build();

  const arrivals = new Map();
  connection.on("ticket.upserted", t => {
    const at = performance.now();
    if (t.lastMessage && arrivals.has(t.lastMessage)) arrivals.get(t.lastMessage)(at);
  });
  await connection.start();

  const samples = [];
  for (let round = 0; round < ROUNDS; round++) {
    const marker = `probe-${Date.now()}-${round}`;
    const waId = `9231000${String(1000 + round).slice(-4)}`;
    const seen = new Promise(resolve => arrivals.set(marker, resolve));

    const sentAt = performance.now();
    await fetch(`${B}/webhook`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ entry: [{ changes: [{ value: {
        metadata: { phone_number_id: "708123456789012" },
        contacts: [{ profile: { name: `Probe ${round}` }, wa_id: waId }],
        messages: [{ id: `wamid.${marker}`, from: waId, timestamp: String(Math.floor(Date.now() / 1000)),
          type: "text", text: { body: marker } }],
      } }] }] }),
    });

    const arrivedAt = await Promise.race([seen, new Promise(r => setTimeout(() => r(null), 15000))]);
    if (arrivedAt) samples.push(arrivedAt - sentAt);
    arrivals.delete(marker);
    await new Promise(r => setTimeout(r, 400));
  }

  console.log(`\nwebhook -> agent screen, ${samples.length}/${ROUNDS} observed`);
  console.log(`  p50 ${percentile(samples, 50).toFixed(0)}ms   p95 ${percentile(samples, 95).toFixed(0)}ms   max ${Math.max(...samples).toFixed(0)}ms`);
  console.log(`  all: ${samples.map(x => x.toFixed(0)).join(", ")}`);
  await connection.stop();
  process.exit(0);
})();
