---
skill: lead-scoring
agents: [Agent-Lead]
owner: PM + Sales
updated: 2026-05-27
---

# Skill — Lead Scoring

## Stage thresholds

| Stage | Score range |
|---|---|
| hot | ≥ 70 |
| warm | 30–69 |
| cold | < 30 |

## Default rules (seed `lead_scoring_rules`)

| Event code | Platform | Weight |
|---|---|---|
| `msg.first_reply` | any | +5 |
| `msg.asks_price` | any | +10 |
| `msg.asks_schedule` | any | +10 |
| `msg.shares_phone` | any | +20 |
| `msg.shares_email` | any | +10 |
| `msg.confirms_trial_lesson` | any | +25 |
| `msg.clicks_quote_link` | any | +15 |
| `comment.purchase_intent` | tiktok | +12 |
| `comment.purchase_intent` | facebook | +10 |
| `lead.no_activity_7d` | any | -10 |
| `lead.no_activity_30d` | any | -25 |

## SLA per stage

- **hot**: assigned within 2 min, sale reply within 5 min.
- **warm**: drip sequence by platform; check-in every 48h.
- **cold**: remarketing weekly, value content monthly.

## Guarantees

- Mọi điểm thay đổi đều append `lead_activities` (audit).
- Re-evaluate on every new `lead_activities` row.
- Không hardcode weight trong code — đọc từ DB.
