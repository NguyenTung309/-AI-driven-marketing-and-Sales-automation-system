---
name: claude-cost-tracker
description: Ledger SQLite local ghi mọi gọi Claude API + USD cost, enforce hard cap $200/tháng/tenant
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    envVars:
      - name: CLAUDE_COST_DB_PATH
        required: false
        description: Đường dẫn SQLite file (default = ./data/claude-cost.db)
    emoji: 💰
    homepage: https://opentelemetry.io/docs/specs/semconv/gen-ai/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-SaleAssist, Agent-Lead, Agent-Content, Agent-Docs, Agent-Ads, Agent-Report, Agent-Research]
    csharp_adapter: Clawbot.Agents.Core.Skills.Ops.IClaudeCostTracker
    owner: P5 PM / Cost
    updated: 2026-05-28
---

# Skill — Claude Cost Tracker

## Purpose
Constitution Art.6: hard cap $200/tháng/tenant + alert 80%. Tracking per-agent để biết agent nào đốt.

## Backing
- SQLite append-only `cost_ledger(tenant_id, agent_code, model, input_tokens, output_tokens, usd_cost, at)`.
- OpenTelemetry `gen_ai.*` semantic conventions (gen_ai.usage.input_tokens, gen_ai.usage.output_tokens, gen_ai.usage.cost).
- Daily roll-up vào `kpi_daily.ad_spend` (cộng dồn cùng cột — đặt nhãn = 'claude').

## Guarantees
- RecordAsync p95 < 5 ms (in-process SQLite).
- SummaryAsync p95 < 50 ms.
- Soft alert 80% → Telegram; hard cap 100% → block agent calls + status `error`.
