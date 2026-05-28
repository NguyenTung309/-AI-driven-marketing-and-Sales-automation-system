---
name: contact-enrichment
description: Tra cứu company/role/social profile từ email hoặc phone (B2B leads)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [HUNTER_API_KEY]
      bins: []
    primaryEnv: HUNTER_API_KEY
    envVars:
      - name: APOLLO_API_KEY
        required: false
        description: Fallback provider khi Hunter không trả result
    emoji: 🔎
    homepage: https://hunter.io/api
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Lead, Agent-SaleAssist]
    csharp_adapter: Clawbot.Agents.Core.Skills.Lead.IContactEnricher
    owner: P5 PM
    updated: 2026-05-27
---

# Skill — Contact Enrichment

## Purpose
Lead phụ huynh đăng ký khóa cho con → tra ngược email/phone để biết company. Lead doanh nhân → biết jobTitle để personalize tư vấn business Chinese.

## Backing
- Primary: Hunter.io `/v2/people/find?email=`.
- Fallback: Apollo.io `/people/match`.
- Cache Redis 24h. Provider cost tracked qua `claude-cost-tracker` style ledger riêng.

## Guarantees
- p95 < 500 ms (network-bound).
- Returns null gracefully khi provider miss.
