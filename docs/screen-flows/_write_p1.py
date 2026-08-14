import os, sys

out = r'E:\DoAn\-AI-driven-marketing-and-Sales-automation-system\docs\screen-flows\USER-FLOWS.md'
os.makedirs(os.path.dirname(out), exist_ok=True)

part1 = "# User Flows Document\n\n"
part1 += "> **Version:** 1.0.0 | **Date:** 2026-08-06 | **Status:** Draft\n"
part1 += "> **Methodology:** authoring-user-flows skill v1.3.0\n\n---\n\n"
part1 += "## Coverage Map\n\n"
part1 += "| Goal / Persona | Flow ID | Flow Name | SRS Scenario |\n"
part1 += "|---|---|---|---|\n"
part1 += "| Secure access (User) | F-01 | System Login and 2FA | SC-05 |\n"
part1 += "| Password reset (User) | F-02 | Password Recovery | SC-05 |\n"
part1 += "| Monitor operations (Admin/Sale) | F-03 | Dashboard Overview | SC-01, SC-02 |\n"
part1 += "| Handle conversations (Sale) | F-04 | Omnichannel Inbox | SC-01, SC-02 |\n"
part1 += "| Manage sales pipeline (Sale) | F-05 | Lead Pipeline Management | SC-06 |\n"
part1 += "| Create content (Marketer) | F-06 | Content Creation and Scheduling | SC-03 |\n"
part1 += "| Manage KB (QA Admin) | F-07 | KB Authoring, Testing and Deployment | SC-05 |\n"
part1 += "| Operate AI agents (Admin) | F-08 | Agent Orchestration and Monitoring | SC-05 |\n"
part1 += "| Generate documents (Sale) | F-09 | Document Generation and Download | SC-05 |\n"
part1 += "| Configure system (Admin) | F-10 | System Settings and Admin | SC-05 |\n\n"
part1 += "---\n\n## Navigation and IA Frame\n\n"
part1 += "### Entry-Point Taxonomy\n\n"
part1 += "| Entry | Landed State | Notes |\n"
part1 += "|---|---|---|\n"
part1 += "| `/login` | LoginPage (P-01) | Public, no auth |\n"
part1 += "| `/forgot-password` | ForgotPasswordPage (P-02) | Public, 4-step wizard |\n"
part1 += "| `/leads/:id` | LeadsPage + LeadDrawer (P-04) | Requires auth; deep-link |\n"
part1 += "| `/conversations/:id` | ConversationsPage (P-05) | Requires auth; deep-link |\n"
part1 += "| `/` after login | DashboardPage (P-03) | Default after login |\n\n"
part1 += "### Navigation / App-Shell Model\n\n"
part1 += "All authenticated pages use AppShell: Sidebar (260px) + Topbar (64px) + main content.\n\n"
part1 += "**Sidebar:** Tong quan, Khach hang tiem nang, Hoi thoai da kenh, Quan ly noi dung, Thu vien tai lieu, Bao cao thong ke, Trung tam thong bao, Agents, Cau hinh LLM, Kho tri thuc. Footer: He thong.\n\n"
part1 += "**Topbar:** search, notification bell (unread badge), active jobs badge, account dropdown.\n\n"
part1 += "### Deep-Linking\n\n"
part1 += "| Route | Target | Guard |\n|---|---|---|\n"
part1 += "| `/leads/:leadId` | P-04 LeadDrawer | Not found -> error, redirect |\n"
part1 += "| `/conversations/:id` | P-05 selected | Not found -> error, show list |\n"
part1 += "| `/agents/runs/:id` | P-16 run detail | Not found -> error, redirect |\n\n"
part1 += "### Cross-Device\n\n"
part1 += "- **Mobile:** Sidebar hamburger. ConversationsPage single-col. LeadDrawer full-screen.\n"
part1 += "- **Tablet:** ConversationsPage 2-col. Context as drawer.\n"
part1 += "- **Desktop (2xl+):** ConversationsPage 3-col (list + chat + context fixed).\n\n---\n\n"

with open(out, 'w', encoding='utf-8') as f:
    f.write(part1)
print("Part 1 done:", len(part1), "chars")
