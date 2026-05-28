---
name: spam-detection
description: Filter junk DM / comment (bot, scam, repeated emoji) trước khi enqueue cho Agent-Chat
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [AKISMET_API_KEY]
      bins: []
    primaryEnv: AKISMET_API_KEY
    emoji: 🛡️
    homepage: https://akismet.com/developers/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-Lead]
    csharp_adapter: Clawbot.Agents.Core.Skills.Lead.ISpamDetector
    owner: P4 QA
    updated: 2026-05-27
---

# Skill — Spam Detection

## Purpose
NFR-04: handle ≥ 500 chat song song. Spam waste agent budget — filter sớm ở Inbox Hub.

## Backing
- Akismet REST API.
- Heuristic fallback offline: regex flood URL, emoji ≥ 10 cùng loại, blacklisted keyword.

## Guarantees
- p95 < 300 ms.
- Spam=true với confidence > 0.9 → tag `conversations.status = 'pending'` + không assign sale.
