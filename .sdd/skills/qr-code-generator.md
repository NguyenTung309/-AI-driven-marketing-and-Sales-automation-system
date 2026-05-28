---
name: qr-code-generator
description: Sinh QR PNG cho Zalo OA / Messenger / link đăng ký, nhúng vào PDF / Story
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    emoji: ▦
    homepage: https://www.nuget.org/packages/QRCoder
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Docs, Agent-Content]
    csharp_adapter: Clawbot.Agents.Core.Skills.Ops.IQrGenerator
    owner: P3 Sales Lead
    updated: 2026-05-27
---

# Skill — QR Code Generator

## Purpose
Footer báo giá PDF + Story IG cần QR Zalo OA. Tạo nhanh, in-process, không call ngoài.

## Backing
- QRCoder NuGet (MIT).

## Guarantees
- 100% sync, p95 < 50 ms.
- ECC level Q mặc định (resilient + reasonable size).
