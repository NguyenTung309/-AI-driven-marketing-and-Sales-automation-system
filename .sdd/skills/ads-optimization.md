---
skill: ads-optimization
agents: [Agent-Ads]
owner: Ads specialist + P2
updated: 2026-05-27
---

# Skill — Ads Optimization

## Thresholds (Meta + TikTok)

| Metric | Comparator | Threshold | Action |
|---|---|---|---|
| CPL | > | 1.5× target | pause adset |
| CPL | < | 0.7× target | scale +20% |
| Frequency | > | 2.0 | rotate creative |
| CTR | < | 0.8% | pause adset |
| Spend | > | 90% daily | alert ops |

## Creative rotation
- Có ≥3 creative active mỗi adset.
- Khi frequency > 2 → đẩy creative cũ off, push hậu bị.

## Scaling rules
- CPL tốt 3 ngày liên tiếp → scale budget +20%/ngày.
- KHÔNG scale >50%/24h (Meta thuật toán reset learning).

## Dayparting
- Pause 02:00–05:00 GMT+7 (low intent).
- Boost 19:00–22:00 (peak intent).

## Lookalike strategy
- Source: học viên đã chốt (≥100 người).
- Refresh hàng tuần.
