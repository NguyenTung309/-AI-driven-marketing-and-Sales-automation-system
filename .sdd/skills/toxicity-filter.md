---
name: toxicity-filter
description: Chặn từ ngữ off-brand / xúc phạm trước khi sale gửi đi
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [DETOXIFY_URL]
      bins: []
    primaryEnv: DETOXIFY_URL
    emoji: 🚫
    homepage: https://github.com/unitaryai/detoxify
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-SaleAssist, Agent-Chat]
    csharp_adapter: Clawbot.Agents.Core.Skills.Nlp.IToxicityFilter
    owner: P4 QA
    updated: 2026-05-27
---

# Skill — Toxicity Filter

## Purpose
Constitution Art.6: agent KHÔNG được dùng từ áp lực / xúc phạm / so sánh đối thủ. Filter cả output Claude lẫn draft sale chỉnh tay.

## Backing
- Detoxify multilingual model (REST sidecar).
- Fallback: Google Perspective API.

## Guarantees
- p95 < 100 ms.
- Threshold mặc định 0.7; admin tune theo tenant.
- Block outbound khi `Toxicity > 0.7` OR `Threat > 0.5`.
