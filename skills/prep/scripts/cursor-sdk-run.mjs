#!/usr/bin/env node

// A single local Cursor SDK run with native, per-turn token accounting.
// Install @cursor/sdk into the ignored run directory before invoking this file.
import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import { mkdtemp, readFile, realpath, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, relative, resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

function optionsFrom(argv) {
  const options = new Map();
  for (let i = 0; i < argv.length; i++) {
    const key = argv[i];
    if (!key?.startsWith("--") || options.has(key)) {
      throw new Error(`Invalid or duplicate argument: ${key ?? "<missing>"}`);
    }
    if (key === "--probe" || key === "--resume") { options.set(key, true); continue; }
    const value = argv[++i];
    if (!value || value.startsWith("--")) throw new Error(`${key} requires a value`);
    options.set(key, value);
  }
  for (const key of ["--project", "--model", "--review-mode", ...(options.has("--probe") ? [] : ["--run-dir"])]) {
    if (!options.has(key)) throw new Error(`${key} is required`);
  }
  for (const key of options.keys()) {
    if (!["--project", "--run-dir", "--model", "--review-mode", "--orchestrate", "--sdk-dir", "--probe", "--resume"].includes(key)) {
      throw new Error(`Unknown argument: ${key}`);
    }
  }
  if (options.has("--probe") && options.has("--resume")) throw new Error("--probe and --resume cannot be combined");
  return options;
}

function command(binary, args) {
  return new Promise((resolveCommand, rejectCommand) => {
    const child = spawn(binary, args, { stdio: ["ignore", "pipe", "pipe"], shell: false });
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", chunk => { stdout += chunk; });
    child.stderr.on("data", chunk => { stderr += chunk; });
    child.on("error", rejectCommand);
    child.on("close", code => {
      if (code === 0) resolveCommand(stdout);
      else rejectCommand(new Error(`orchestrate heartbeat command failed (exit ${code}): ${stderr.trim() || "no details"}`));
    });
  });
}

function nativeTotal(usage) {
  const count = usage?.totalTokens;
  if (!Number.isSafeInteger(count) || count < 0) return null;
  return count;
}

class UsageTracker {
  constructor(runId) {
    this.runId = runId;
    this.turnTotal = 0;
    this.total = null;
  }

  recordTurn(event, run) {
    if (event.type !== "usage" || event.run_id !== this.runId) return null;
    const count = nativeTotal(event.usage);
    if (count === null) return null;
    this.turnTotal += count;
    if (!Number.isSafeInteger(this.turnTotal)) throw new Error("Cursor usage exceeded JavaScript's exact integer range");
    this.total = Math.max(this.total ?? 0, this.turnTotal, nativeTotal(run.usage) ?? 0);
    return this.total;
  }

  recordFinal(result, run) {
    const cumulative = nativeTotal(result.usage) ?? nativeTotal(run.usage);
    if (cumulative !== null) this.total = Math.max(this.total ?? 0, this.turnTotal, cumulative);
    return this.total;
  }
}

async function loadSdk(sdkDirectory) {
  const requireFromSdk = createRequire(join(sdkDirectory, "package.json"));
  let entry;
  try { entry = requireFromSdk.resolve("@cursor/sdk"); }
  catch { throw new Error(`@cursor/sdk is unavailable in ${sdkDirectory}; install it there before launch`); }
  const module = await import(pathToFileURL(entry).href);
  const Agent = module.Agent ?? module.default?.Agent;
  const JsonlLocalAgentStore = module.JsonlLocalAgentStore ?? module.default?.JsonlLocalAgentStore;
  if (!Agent || !JsonlLocalAgentStore) throw new Error("Installed @cursor/sdk does not expose the required local Agent and JSONL store APIs");
  return { Agent, JsonlLocalAgentStore };
}

async function main() {
  const options = optionsFrom(process.argv.slice(2));
  const probe = options.has("--probe");
  const resume = options.has("--resume");
  const project = await realpath(resolve(options.get("--project")));
  const runDirectory = options.has("--run-dir") ? await realpath(resolve(options.get("--run-dir"))) : null;
  if (runDirectory) {
    const expectedRuns = join(project, ".orchestra", "runs");
    const runRelative = relative(expectedRuns, runDirectory);
    if (!runRelative || runRelative.startsWith(".." + sep) || runRelative === ".." || resolve(expectedRuns, runRelative) !== runDirectory) {
      throw new Error("--run-dir must be a run inside the selected project's .orchestra/runs directory");
    }
  }
  const sdkDirectory = await realpath(resolve(options.get("--sdk-dir") ?? runDirectory ?? project));
  const orchestrate = options.get("--orchestrate") ?? "orchestrate";
  const model = options.get("--model");
  const reviewMode = options.get("--review-mode");
  if (!["sandbox", "auto-review"].includes(reviewMode)) {
    throw new Error("--review-mode must be sandbox or auto-review, matching the prepared run permission decision");
  }
  let kickoffPrompt;
  let kickoffCommand;
  if (!probe) {
    [kickoffPrompt, kickoffCommand] = await Promise.all([
      readFile(join(runDirectory, "kickoff-prompt.md"), "utf8"),
      readFile(join(project, ".cursor", "commands", "kickoff.md"), "utf8"),
    ]);
    if (!kickoffPrompt.trim() || !kickoffCommand.trim()) throw new Error("The kickoff prompt and Cursor command must be nonempty");
  }

  const { Agent, JsonlLocalAgentStore } = await loadSdk(sdkDirectory);
  const storeDirectory = probe ? await mkdtemp(join(tmpdir(), "orchestra-cursor-probe-")) : join(runDirectory, "cursor-sdk-state");
  const store = new JsonlLocalAgentStore(storeDirectory);
  let agent;
  let run;
  try {
    const agentOptions = {
      ...(process.env.CURSOR_API_KEY ? { apiKey: process.env.CURSOR_API_KEY } : {}),
      model: { id: model },
      mode: "agent",
      ...(probe ? { tools: [] } : {}),
      local: {
        cwd: project,
        store,
        sandboxOptions: { enabled: true },
        autoReview: reviewMode === "auto-review",
      },
    };
    if (probe) {
      agent = await Agent.create(agentOptions);
      run = await agent.send("Reply with exactly READY. Do not use tools, read files, or change files.");
      const usage = new UsageTracker(run.id);
      for await (const event of run.stream()) usage.recordTurn(event, run);
      const result = await run.wait();
      if (result.status !== "finished") throw new Error("Cursor SDK usage probe did not finish successfully");
      const measured = usage.recordFinal(result, run);
      if (measured === null || measured === 0) throw new Error("Cursor SDK supplied no positive native token usage during probe");
      process.stdout.write(`ORCHESTRA_CURSOR_USAGE_PROBE=ok tokens=${measured}\n`);
      return;
    }
    let priorSessionId = null;
    const status = JSON.parse(await command(orchestrate, ["heartbeat", "status", "--dir", runDirectory]));
    if (status.usageSource !== null && status.usageSource !== undefined && status.usageSource !== "cursor-sdk") {
      throw new Error("Prepared run is already measured by another token source");
    }
    if (resume) {
      priorSessionId = status.boundSessionId;
      if (typeof priorSessionId !== "string" || !priorSessionId) throw new Error("No prior bound Cursor SDK run is available to resume");
      if (status.stopRecommended === true) throw new Error("Prepared run has reached a stop condition; resolve it before resuming");
      const priorRun = await Agent.getRun(priorSessionId, { runtime: "local", cwd: project, store });
      if (!["finished", "error", "cancelled"].includes(priorRun.status)) {
        throw new Error("Prior Cursor SDK run is active or its terminal status is unknown; refusing to replace it");
      }
      if (typeof priorRun.agentId !== "string" || !priorRun.agentId) throw new Error("Prior Cursor SDK run has no agent ID");
      const priorResult = await priorRun.wait();
      const priorTokens = nativeTotal(priorResult.usage) ?? nativeTotal(priorRun.usage);
      if (priorTokens !== null) {
        await command(orchestrate, ["heartbeat", "usage", "--dir", runDirectory, "--source", "cursor-sdk", "--session-id", priorSessionId, "--tokens-used", String(priorTokens)]);
      } else if (status.tokenBudget !== null && status.tokenBudget !== undefined) {
        throw new Error("Prior Cursor SDK run has no final native usage; cannot enforce cumulative token cap");
      }
      const refreshed = JSON.parse(await command(orchestrate, ["heartbeat", "status", "--dir", runDirectory]));
      if (refreshed.boundSessionId !== priorSessionId || refreshed.stopRecommended === true) {
        throw new Error("Prepared run changed or reached a stop condition during resume preflight");
      }
      agent = await Agent.resume(priorRun.agentId, agentOptions);
    } else {
      if (status.boundSessionId !== null && status.boundSessionId !== undefined) {
        throw new Error("Prepared run already has a bound session; use --resume after it ends");
      }
      agent = await Agent.create(agentOptions);
    }
    // Slash commands are a Cursor UI feature. Supplying the installed command
    // body makes SDK behavior explicit, while retaining the prepared handoff.
    const continuation = resume ? "Resume this prepared run from its brief and progress ledger. Continue the next ready task without repeating completed work.\n\n" : "";
    const prompt = `${continuation}${kickoffPrompt.trim()}\n\nFollow the Cursor kickoff command below for ${runDirectory}:\n\n${kickoffCommand}`;
    run = await agent.send(prompt);
    if (typeof run.id !== "string" || !run.id.trim() || run.id.length > 128 || /[\u0000-\u001f\u007f]/u.test(run.id)) {
      throw new Error("Cursor SDK did not return a valid run ID");
    }
    await command(orchestrate, ["heartbeat", "bind", "--dir", runDirectory, "--session-id", run.id,
      ...(resume ? ["--replace", "--expected-session-id", priorSessionId] : [])]);
    process.stdout.write(`ORCHESTRA_CURSOR_SESSION_ID=${run.id}\n`);

    const usage = new UsageTracker(run.id);
    let reportedTotal = null;
    let stoppedForBudget = false;
    const report = async candidate => {
      if (candidate === null || (reportedTotal !== null && candidate <= reportedTotal)) return false;
      await command(orchestrate, ["heartbeat", "usage", "--dir", runDirectory, "--source", "cursor-sdk", "--session-id", run.id, "--tokens-used", String(candidate)]);
      reportedTotal = candidate;
      const status = JSON.parse(await command(orchestrate, ["heartbeat", "status", "--dir", runDirectory]));
      return status.tokensOverBudget === true;
    };

    for await (const event of run.stream()) {
      if (await report(usage.recordTurn(event, run))) {
        stoppedForBudget = true;
        await run.cancel();
        break;
      }
    }
    const result = await run.wait();
    if (await report(usage.recordFinal(result, run))) stoppedForBudget = true;
    if (reportedTotal === null) {
      if (status.tokenBudget !== null && status.tokenBudget !== undefined) {
        throw new Error("Capped Cursor SDK run supplied no native token usage; further unattended work is unsafe");
      }
      process.stderr.write("Cursor SDK supplied no token usage; dashboard usage remains unavailable.\n");
    }
    process.stdout.write(`ORCHESTRA_CURSOR_STATUS=${stoppedForBudget ? "budget-stopped" : result.status}\n`);
    if (result.status === "error") throw new Error(`Cursor SDK run ${run.id} failed`);
  } catch (error) {
    if (run && run.status === "running") await run.cancel().catch(() => {});
    throw error;
  } finally {
    try { if (agent) await agent[Symbol.asyncDispose](); }
    finally { if (probe) await rm(storeDirectory, { recursive: true, force: true }); }
  }
}

main().catch(error => {
  // Do not print SDK event payloads, prompts, API keys, or agent transcripts.
  process.stderr.write(`Cursor measured run: ${error instanceof Error ? error.message : "unknown error"}\n`);
  process.exitCode = 1;
});
