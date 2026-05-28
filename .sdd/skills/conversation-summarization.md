---
name: conversation-summarization
description: Tóm tắt thread chat dài (>20 turn) cho Sale Assist trước khi sale đọc
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [ANTHROPIC_API_KEY]
      bins: []
    primaryEnv: ANTHROPIC_API_KEY
    emoji: 📝
    homepage: https://docs.anthropic.com/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-SaleAssist, Agent-Chat]
    csharp_adapter: Clawbot.Agents.Core.Skills.Nlp.IConversationSummarizer
    owner: P4 QA
    updated: 2026-05-27
---

# Skill — Conversation Summarization

## Purpose
Sale mới vào ca cần grasp context nhanh: thread > 20 turn → tóm tắt 3-5 câu + 3 key points.

## Backing
- Claude Sonnet 4.6 via Semantic Kernel chat completion.
- Cached per (conversation_id, last_msg_id) trong Redis TTL 6h.

## Guarantees
- p95 < 3s.
- Output dạng `{ summary, key_points[] }` JSON schema-enforced.
