---
name: prompt-injection-defender
description: Phát hiện prompt-injection / jailbreak trong inbound text trước khi đưa vào Agent-Chat / Agent-SaleAssist
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [LAKERA_API_KEY]
      bins: []
    primaryEnv: LAKERA_API_KEY
    envVars:
      - name: LLMGUARD_URL
        required: false
        description: Self-hosted protectai/llm-guard fallback
    emoji: 🛡️
    homepage: https://www.lakera.ai/lakera-guard
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-SaleAssist, Agent-Lead]
    csharp_adapter: Clawbot.Agents.Core.Skills.Ops.IPromptInjectionDefender
    owner: P5 PM / Security
    updated: 2026-05-28
---

# Skill — Prompt Injection Defender

## Purpose
Constitution Art.3 + NFR-03: block "Ignore previous instructions" / data-exfil patterns / role-override trước khi pass tới Claude.

## Backing
- Lakera Guard REST API.
- Fallback: protectai/llm-guard self-hosted.

## Guarantees
- p95 < 200 ms.
- IsMalicious=true → 422 cho user + log `audit_logs.action = 'security.prompt_injection_blocked'`.
