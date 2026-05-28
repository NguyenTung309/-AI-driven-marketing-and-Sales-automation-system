---
name: lead-deduplication
description: Stitch identity cross-platform khi khách nhắn từ nhiều kênh (UC-A05, UC-D04)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [QDRANT_URL]
      bins: []
    primaryEnv: QDRANT_URL
    emoji: 🧬
    homepage: https://qdrant.tech/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Lead]
    csharp_adapter: Clawbot.Agents.Core.Skills.Lead.ILeadDeduplicator
    owner: P5 PM
    updated: 2026-05-27
---

# Skill — Lead Deduplication

## Purpose
Đi xa hơn `(platform, external_id)` exact match: dùng cosine similarity trên embedding của (display_name + phone tail + email + zip) → tìm 5 candidate gần nhất → confirm dedup khi similarity ≥ 0.92.

## Backing
- Qdrant collection `contacts` (point id = `contacts.id`, vector dim 1536).
- Embedding bằng Claude `voyage-3` hoặc OpenAI `text-embedding-3-small`.

## Guarantees
- p95 < 200 ms.
- Audit kết quả vào `audit_logs.action = 'contact.dedup_merge'` với diff JSON.
