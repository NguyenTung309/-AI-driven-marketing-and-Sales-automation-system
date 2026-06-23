import pathlib

# 1. lazyPages.tsx
p = pathlib.Path("D:/Clawbot _ demo/-AI-driven-marketing-and-Sales-automation-system/src/frontend/clawbot-web/src/app/lazyPages.tsx")
content = p.read_text(encoding="utf-8")
content = content.rstrip() + '\nexport const AgentHubLayout = lazy(() => import("@/features/agent-hub/AgentHubLayout"));\n'
p.write_text(content, encoding="utf-8")
print("lazyPages done")

# 2. routes.tsx
p = pathlib.Path("D:/Clawbot _ demo/-AI-driven-marketing-and-Sales-automation-system/src/frontend/clawbot-web/src/app/routes.tsx")
content = p.read_text(encoding="utf-8")
old = "  ChannelManagementPage,\n} from \"./lazyPages\";"
new = "  ChannelManagementPage,\n  AgentHubLayout,\n} from \"./lazyPages\";"
content = content.replace(old, new)
old = '    path: "/system/channels",\n    element: (\n      <RequireAuth>\n        <ChannelManagementPage />\n      </RequireAuth>\n    ),\n  },\n]);'
new = '    path: "/system/channels",\n    element: (\n      <RequireAuth>\n        <ChannelManagementPage />\n      </RequireAuth>\n    ),\n  },\n  {\n    path: "/agent-hub",\n    element: (\n      <RequireAuth>\n        <AgentHubLayout />\n      </RequireAuth>\n    ),\n  },\n]);'
content = content.replace(old, new)
p.write_text(content, encoding="utf-8")
print("routes done")

# 3. nav.ts
p = pathlib.Path("D:/Clawbot _ demo/-AI-driven-marketing-and-Sales-automation-system/src/frontend/clawbot-web/src/shared/layout/nav.ts")
content = p.read_text(encoding="utf-8")
old = '  { icon: "inventory_2", label: "Kho tri thuc", to: "/kb" },'
new = '  { icon: "smart_toy", label: "Agent Hub", to: "/agent-hub" },\n  { icon: "inventory_2", label: "Kho tri thuc", to: "/kb" },'
content = content.replace(old, new)
p.write_text(content, encoding="utf-8")
print("nav done")
