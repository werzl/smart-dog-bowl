import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

const source = await readFile(new URL("../public/sw.js", import.meta.url), "utf8");
function worker({ items = [], offline = false, windows = [] } = {}) {
  const handlers = new Map();
  const notifications = [];
  const opened = [];
  const self = {
    addEventListener: (name, handler) => handlers.set(name, handler),
    registration: { showNotification: async (title, options) => notifications.push({ title, ...options }) },
    clients: {
      matchAll: async () => windows,
      openWindow: async url => opened.push(url),
      claim: async () => {},
    },
    location: { origin: "https://bowl.example.org" },
    skipWaiting: () => {},
  };
  vm.runInNewContext(source, {
    self, URL, Date, AbortSignal,
    fetch: async () => {
      if (offline) throw new Error("The API is unavailable.");
      return { ok: true, json: async () => items };
    },
  });
  return {
    notifications, opened,
    async dispatch(name, event) {
      let completion;
      handlers.get(name)({ ...event, waitUntil: promise => { completion = promise; } });
      await completion;
    },
  };
}
const payload = { id: "alert-1", title: "Empty bowl reported", body: "Kitchen bowl needs water.", createdAt: "2026-11-21T12:00:00Z" };

test("An active alert shows a notification and refreshes open apps", async () => {
  const messages = [];
  const instance = worker({ items: [{ id: payload.id, resolvedAt: null }], windows: [{ postMessage: value => messages.push(value) }] });
  await instance.dispatch("push", { data: { json: () => payload } });
  assert.equal(instance.notifications[0].title, payload.title);
  assert.equal(instance.notifications[0].tag, "bowl-alert-1");
  assert.equal(instance.notifications[0].timestamp, Date.parse(payload.createdAt));
  assert.equal(messages[0].type, "bowl-notification");
});

test("A delayed push shows the refill when the API reports it", async () => {
  const instance = worker({ items: [{ id: payload.id, resolvedAt: "2026-11-21T12:01:00Z" }] });
  await instance.dispatch("push", { data: { json: () => payload } });
  assert.equal(instance.notifications[0].title, "Bowl refilled");
});

test("An unavailable API retains the report and its original timestamp", async () => {
  const instance = worker({ offline: true });
  await instance.dispatch("push", { data: { json: () => payload } });
  assert.equal(instance.notifications[0].title, payload.title);
  assert.equal(instance.notifications[0].timestamp, Date.parse(payload.createdAt));
});

test("A malformed payload still produces a visible notification", async () => {
  const instance = worker();
  await instance.dispatch("push", { data: { json: () => { throw new Error("Invalid JSON."); } } });
  assert.equal(instance.notifications[0].title, "Smart Bowl notification");
});

test("A null payload still produces a visible notification", async () => {
  const instance = worker();
  await instance.dispatch("push", { data: { json: () => null } });
  assert.equal(instance.notifications[0].title, "Smart Bowl notification");
});

test("A notification opens the overview and ignores payload URLs", async () => {
  const instance = worker();
  let closed = false;
  await instance.dispatch("notificationclick", { notification: { close: () => { closed = true; }, data: { url: "https://other.example.org" } } });
  assert.equal(closed, true);
  assert.deepEqual(instance.opened, ["/#overview"]);
});

test("A notification focuses an existing app window", async () => {
  const actions = [];
  const instance = worker({ windows: [{ url: "https://bowl.example.org/#history", navigate: async url => actions.push(url), focus: async () => actions.push("focus") }] });
  await instance.dispatch("notificationclick", { notification: { close: () => {} } });
  assert.deepEqual(actions, ["/#overview", "focus"]);
  assert.deepEqual(instance.opened, []);
});
