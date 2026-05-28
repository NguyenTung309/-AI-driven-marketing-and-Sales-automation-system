---
name: short-video-script-formula
description: Compose TikTok/Reels script theo công thức Hook + Value + CTA (1 trang JSON)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [ANTHROPIC_API_KEY]
      bins: []
    primaryEnv: ANTHROPIC_API_KEY
    emoji: 🎬
    homepage: https://docs.anthropic.com/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Content]
    csharp_adapter: Clawbot.Agents.Core.Skills.Content.IVideoScriptComposer
    owner: P2 Growth
    updated: 2026-05-27
---

# Skill — Short Video Script Formula

## Purpose
Mỗi content brief platform=tiktok → script chuẩn 3 phần (Hook 3s + Value ~25s + CTA 2s) + shot list 5-7 cảnh.

## Backing
- Claude Sonnet 4.6 với JSON schema response_format.
- Reference: `.sdd/skills/content-copywriting.md` (hook formula + banned phrases).

## Guarantees
- p95 < 3s.
- Output JSON schema validation strict — reject nếu thiếu hook/value/cta/shot_list.
