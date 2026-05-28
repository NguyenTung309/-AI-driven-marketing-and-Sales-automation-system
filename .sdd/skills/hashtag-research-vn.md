---
name: hashtag-research-vn
description: Top trending hashtag VN trên TikTok/Instagram/YouTube cho weekly content brief
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    envVars:
      - name: TIKTOK_CC_COOKIE
        required: false
        description: Auth cookie cho TikTok Creative Center (bypass anonymous rate limit)
    emoji: '#'
    homepage: https://ads.tiktok.com/business/creativecenter
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Research, Agent-Content]
    csharp_adapter: Clawbot.Agents.Core.Skills.Content.IHashtagResearcher
    owner: P2 Growth
    updated: 2026-05-27
---

# Skill — Hashtag Research VN

## Purpose
Mỗi thứ 2: lấy top 20 hashtag trending VN trên 5 platform → feed Agent-Research weekly trend (UC-E01).

## Backing
- TikTok Creative Center scrape (JSON endpoint).
- Google Trends VN cho keyword cross-reference.
- Cache Redis 6h.

## Guarantees
- p95 < 2s (multi-platform fan-out).
- Filter banned topics (theo `.sdd/skills/trend-tieng-trung.md` exclude list).
