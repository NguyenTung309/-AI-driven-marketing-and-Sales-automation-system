---
name: pii-redaction
description: Mask phone/email/SĐT/CCCD trong message log + audit trail (NFR-03 compliance)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [PRESIDIO_ANALYZER_URL, PRESIDIO_ANONYMIZER_URL]
      bins: []
    primaryEnv: PRESIDIO_ANALYZER_URL
    emoji: 🔒
    homepage: https://github.com/microsoft/presidio
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-SaleAssist, Agent-Report]
    csharp_adapter: Clawbot.Agents.Core.Skills.Nlp.IPiiRedactor
    owner: P5 PM / Security
    updated: 2026-05-27
---

# Skill — PII Redaction

## Purpose
NFR-03: không lưu PII thô trong log > 30 ngày. Mask phone (`0[3|5|7|8|9]xxxxxxxx`), email, CCCD (12 số), số thẻ trước khi append vào `audit_logs` / external sinks.

## Backing
- Microsoft Presidio (Analyzer + Anonymizer REST sidecar).
- Add custom Vietnamese recognizers: phone regex VN, CCCD checksum.

## Guarantees
- p95 < 100 ms per message.
- Returns redacted text + offset list (spans) để revert nếu cần.
