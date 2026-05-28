---
skill: trend-tieng-trung
agents: [Agent-Research]
owner: Content + Research
updated: 2026-05-27
---

# Skill — Trend tiếng Trung VN

## Sources

- TikTok trending sounds + hashtags (region VN)
- YouTube Vietnam trending (category: Education, Entertainment)
- Baidu / Weibo trending (filter cultural-export topics relevant to VN learners)
- Google Trends (region VN, keywords: học tiếng Trung, HSK, 中文)

## Cadence

- **Mondays 07:00 GMT+7** — Agent-Research scrapes + summarizes.
- Output: top 5 trends + 20 content ideas mapped to 5 platforms.
- Posts to Notion content queue (`content_briefs`).

## Topic filters (include)

- HSK exam tips, vocab tricks
- Chinese drama / song trending in VN
- Travel China (Tết, summer)
- Business Mandarin
- K-pop ↔ C-pop crossover hooks
- Trẻ em learning gamification

## Topic filters (exclude)

- Political content
- Anti-China sentiment, controversial geopolitics
- Health / medical claims

## Output schema

```json
{
  "week_of": "2026-W22",
  "trends": [
    {
      "topic": "...",
      "source": "tiktok",
      "metric": "1.2M views in 24h",
      "relevance_score": 0.85,
      "content_ideas": [
        {"platform": "tiktok", "hook": "...", "cta": "..."}
      ]
    }
  ]
}
```
