---
name: timezone-detection
description: Đoán timezone của contact từ phone country code / locale / explicit field để route drip + reminder
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    emoji: 🕒
    homepage: https://nodatime.org
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Lead, Agent-Chat]
    csharp_adapter: Clawbot.Agents.Core.Skills.Lead.ITimezoneDetector
    owner: P5 PM
    updated: 2026-05-27
---

# Skill — Timezone Detection

## Purpose
Drip sequence (UC-F01..F08) cần gửi đúng giờ local. Phone +84 → Asia/Ho_Chi_Minh; +86 → Asia/Shanghai; default fallback theo `tenants.settings_json.default_tz`.

## Backing
- NodaTime DateTimeZoneProviders.
- libphonenumber-csharp để parse phone country code.

## Guarantees
- 100% sync (no I/O).
- p95 < 5 ms.
