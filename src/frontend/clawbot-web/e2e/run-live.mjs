process.env.E2E_LIVE = "1";
import { spawn } from "node:child_process";

const child = spawn("npx", ["playwright", "test", ...process.argv.slice(2)], {
  stdio: "inherit",
  shell: true,
  env: process.env,
  cwd: new URL("..", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"),
});

child.on("exit", (code) => process.exit(code ?? 1));
