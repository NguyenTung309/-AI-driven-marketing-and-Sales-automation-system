---
name: zh-script-validation
description: Đảm bảo content Chinese dùng đúng 简体 hoặc 繁體 theo target audience, convert 2 chiều
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: [opencc]
    emoji: 漢
    homepage: https://github.com/BYVoid/OpenCC
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Content, Agent-Chat]
    csharp_adapter: Clawbot.Agents.Core.Skills.Content.IZhScriptValidator
    owner: P4 QA
    updated: 2026-05-27
---

# Skill — ZH Script Validation

## Purpose
KB tiếng Trung mix simplified/traditional sẽ confuse learner. Mỗi `kb_versions` deploy cần check consistency. Content TikTok nhắm học viên VN → default Simplified.

## Backing
- OpenCC binary (s2t.json, t2s.json) hoặc OpenCC.NET wrapper.

## Guarantees
- 100% deterministic.
- p95 < 50 ms cho text < 5k chars.
