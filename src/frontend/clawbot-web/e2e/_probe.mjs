console.log("probe-start");
try {
  const mod = await import("@playwright/test");
  console.log("import-ok", Object.keys(mod).slice(0, 8).join(","));
} catch (error) {
  console.error("import-fail", error);
  process.exit(1);
}
console.log("probe-end");
