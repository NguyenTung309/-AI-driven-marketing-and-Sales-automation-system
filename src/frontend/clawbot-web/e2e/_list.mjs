import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const cli = path.join(root, "node_modules", "@playwright", "test", "cli.js");

console.log("spawning", cli);
const child = spawn(
  process.execPath,
  [cli, "test", "--config=playwright.config.mjs", "--list"],
  {
    cwd: root,
    env: process.env,
    stdio: "inherit",
  },
);

child.on("exit", (code, signal) => {
  console.log("child-exit", code, signal);
  process.exit(code ?? 1);
});
