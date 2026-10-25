// Local analysis only. Emits aggregate timings, never URLs, paths, titles or raw arguments.
// Usage: node analyze-trace.mjs <chromium-trace.json> [summary.json]
import fs from "node:fs";
import crypto from "node:crypto";

const bytes = fs.readFileSync(process.argv[2]);
const trace = JSON.parse(bytes);
const events = trace.traceEvents;
let start = Infinity,
  end = 0;
for (const e of events)
  if (e.ts > 0) {
    start = Math.min(start, e.ts);
    end = Math.max(end, e.ts + (e.dur ?? 0));
  }
const round = (n) => Math.round(n * 1000) / 1000;
const seconds = (ts) => round((ts - start) / 1e6);
const count = (values) => {
  const result = {};
  for (const value of values) result[value] = (result[value] ?? 0) + 1;
  return result;
};
const stats = (values) => {
  const sorted = values.toSorted((a, b) => a - b);
  const q = (p) =>
    round(sorted[Math.max(0, Math.ceil(sorted.length * p) - 1)] ?? 0);
  return {
    n: sorted.length,
    total: round(sorted.reduce((a, b) => a + b, 0)),
    p50: q(0.5),
    p95: q(0.95),
    p99: q(0.99),
    max: q(1),
  };
};
const named = (name) => events.filter((e) => e.name === name);
const threadNames = new Map(
  events
    .filter((e) => e.name === "thread_name")
    .map((e) => [`${e.pid}:${e.tid}`, e.args.name]),
);
const main = events.filter(
  (e) => threadNames.get(`${e.pid}:${e.tid}`) === "CrRendererMain",
);
const xMain = main.filter((e) => e.ph === "X");
const functionLabel = (e) => {
  const data = e.args?.data;
  const bundle = data?.url?.startsWith("winnow-app://app/assets/")
    ? data.url.split("/").at(-1)
    : "(other script)";
  return `${data?.functionName || "(anonymous)"} at ${bundle}:${data?.lineNumber ?? "?"}`;
};
const display = named("Display::FrameDisplayed").toSorted(
  (a, b) => a.ts - b.ts,
);
const gaps = display
  .slice(1)
  .map((e, i) => ({ at: seconds(e.ts), ms: (e.ts - display[i].ts) / 1000 }));
const summarizeDurations = (items) => {
  const groups = new Map();
  for (const e of items) {
    const name = e.name === "FunctionCall" ? functionLabel(e) : e.name;
    if (!groups.has(name)) groups.set(name, []);
    groups.get(name).push(e.dur / 1000);
  }
  return [...groups]
    .map(([name, values]) => ({ name, ...stats(values) }))
    .sort((a, b) => b.total - a.total);
};
const present = named("DXGISwapChainImageBacking::Present");
const begin = named("ExternalBeginFrameSource::OnBeginFrame").filter(
  (e) => e.args?.begin_frame_args,
);
const bufferStats = trace.metadata?.trace_processor_stats?.traced_buf;
const animationStarts = named("Animation").filter((e) => e.ph === "b");
const detailWindows = [
  [9.8, 11],
  [13.24, 14],
].map(([from, to]) => {
  const inWindow = (e) => e.ts >= start + from * 1e6 && e.ts < start + to * 1e6;
  const surfaces = {};
  for (const e of named("RenderSurfaceReasonCount").filter(inWindow))
    for (const [key, value] of Object.entries(e.args))
      surfaces[key] = Math.max(surfaces[key] ?? 0, value);
  return {
    from,
    to,
    surfaceReasonMaxima: surfaces,
    reportedFrames: display.filter(inWindow).length,
    renderPasses: named("DirectRenderer::DrawRenderPass").filter(inWindow)
      .length,
    drawAndSwapMs: stats(
      named("Display::DrawAndSwap")
        .filter((e) => e.ph === "X" && inWindow(e))
        .map((e) => e.dur / 1000),
    ),
    mainFrameMs: stats(
      xMain
        .filter((e) => e.name === "ProxyMain::BeginMainFrame" && inWindow(e))
        .map((e) => e.dur / 1000),
    ),
  };
});
const report = {
  format:
    "All durations ms, offsets seconds. Nested duration totals are inclusive and must not be added together. Display feedback intervals are not monitor scanout or dropped-frame counts.",
  input: {
    bytes: bytes.length,
    sha256: crypto.createHash("sha256").update(bytes).digest("hex"),
    events: events.length,
    durationSeconds: seconds(end),
    product: trace.metadata?.["product-version"],
    buffers: bufferStats,
  },
  presentation: {
    directCompositionPresents: named("DCompPresenter::Present").length,
    reportedFrames: display.length,
    intervalsMs: stats(gaps.map((g) => g.ms)),
    longestGaps: gaps
      .toSorted((a, b) => b.ms - a.ms)
      .slice(0, 12)
      .map((g) => ({ at: g.at, ms: round(g.ms) })),
    requestedIntervalsUs: count(
      begin.map((e) => e.args.begin_frame_args.interval_delta_us),
    ),
    swapChainPresentMs: stats(present.map((e) => e.dur / 1000)),
    pendingFrames: count(
      named("DCompPresenter::CheckPendingFrames").map(
        (e) => e.args.num_pending_frames,
      ),
    ),
    resizes: named("SkiaOutputDeviceDComp::BeginOverlayAccess").map((e) => ({
      at: seconds(e.ts),
      size: e.args.image_size,
    })),
  },
  detailWindows,
  perSecond: Array.from({ length: Math.ceil((end - start) / 1e6) }, (_, i) => {
    const inBin = (e) =>
      e.ts >= start + i * 1e6 && e.ts < start + (i + 1) * 1e6;
    const xs = xMain.filter(inBin);
    const frameReports = named("PipelineReporter").filter(
      (e) =>
        e.ph === "b" &&
        e.args?.frame_reporter &&
        threadNames.get(`${e.pid}:${e.tid}`) === "Compositor" &&
        inBin(e),
    );
    return {
      second: i,
      displayed: display.filter(inBin).length,
      beginIntervalsUs: count(
        begin
          .filter(inBin)
          .map((e) => e.args.begin_frame_args.interval_delta_us),
      ),
      mainFrameMs: stats(
        xs
          .filter((e) => e.name === "ProxyMain::BeginMainFrame")
          .map((e) => e.dur / 1000),
      ),
      functionMs: stats(
        xs.filter((e) => e.name === "FunctionCall").map((e) => e.dur / 1000),
      ),
      paintMs: stats(
        xs.filter((e) => e.name === "Paint").map((e) => e.dur / 1000),
      ),
      renderPasses: named("DirectRenderer::DrawRenderPass").filter(inBin)
        .length,
      pipelineStates: count(
        frameReports.map((e) => e.args.frame_reporter.state),
      ),
    };
  }),
  mainThreadFunctions: summarizeDurations(
    xMain.filter((e) => e.name === "FunctionCall"),
  ).slice(0, 20),
  mainThreadPhases: summarizeDurations(
    xMain.filter((e) => e.name !== "FunctionCall"),
  ).slice(0, 24),
  longestFunctions: xMain
    .filter((e) => e.name === "FunctionCall")
    .sort((a, b) => b.dur - a.dur)
    .slice(0, 12)
    .map((e) => ({
      at: seconds(e.ts),
      ms: e.dur / 1000,
      cpuMs: (e.tdur ?? 0) / 1000,
      name: e.args?.data?.functionName || "(anonymous)",
      line: e.args?.data?.lineNumber,
    })),
  interactions: main
    .filter(
      (e) =>
        e.name === "EventDispatch" &&
        ["click", "keydown"].includes(e.args?.data?.type),
    )
    .map((e) => ({ at: seconds(e.ts), type: e.args.data.type })),
  animations: {
    totalStarts: animationStarts.length,
    byName: count(
      animationStarts.map((e) => e.args?.data?.displayName ?? "(unnamed)"),
    ),
  },
  contextEvents: Object.fromEntries(
    [
      "GpuChannel::CreateCommandBuffer",
      "GpuChannel::OnDestroyCommandBuffer",
      "LaunchGpuProcess",
    ].map((name) => [name, named(name).map((e) => seconds(e.ts))]),
  ),
};
const output = JSON.stringify(report, null, 2) + "\n";
if (process.argv[3]) fs.writeFileSync(process.argv[3], output);
else process.stdout.write(output);
