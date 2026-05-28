# ClawBot Skill Catalog

> 31 skill = 9 prompt/process knowledge (Phase 1) + 22 utility/library-backed (Phase 2). Mỗi skill có 1 SKILL.md trong thư mục này. Skill utility kèm C# adapter trong `src/agents/Clawbot.Agents.Core/Skills/`.

## Phase 1 — Prompt / Process knowledge (9)

| Skill | Agents | File |
|---|---|---|
| Knowledge Base tiếng Trung | Chat, SaleAssist | [knowledge-base-tieng-trung.md](knowledge-base-tieng-trung.md) |
| 50 Chat Scenarios | Chat, SaleAssist | [50-chat-scenarios.md](50-chat-scenarios.md) |
| Zalo Sales Consultation | Chat, SaleAssist | [zalo-sales-consultation.md](zalo-sales-consultation.md) |
| Platform Specs | Chat, Content | [platform-specs.md](platform-specs.md) |
| Ads Optimization | Ads | [ads-optimization.md](ads-optimization.md) |
| Content Copywriting | Content | [content-copywriting.md](content-copywriting.md) |
| Doc Templates | Docs | [doc-templates.md](doc-templates.md) |
| Lead Scoring | Lead | [lead-scoring.md](lead-scoring.md) |
| Trend tiếng Trung VN | Research | [trend-tieng-trung.md](trend-tieng-trung.md) |

## Phase 2 — Utility / library-backed (22) — NEW

### NLP (6) — `Clawbot.Agents.Core.Skills.Nlp.*`

| Skill | Agents | C# adapter | Source |
|---|---|---|---|
| Intent Classification | Chat, SaleAssist | `IIntentClassifier` | vinai/phobert-base-v2 |
| Sentiment Analysis | Chat, SaleAssist, Report | `ISentimentAnalyzer` | wonrax/phobert-base-vietnamese-sentiment |
| Language Detection | Chat, SaleAssist | `ILanguageDetector` | fasttext lid.176 |
| PII Redaction | Chat, SaleAssist, Report | `IPiiRedactor` | microsoft/presidio |
| Toxicity Filter | SaleAssist, Chat | `IToxicityFilter` | unitaryai/detoxify |
| Conversation Summarization | SaleAssist, Chat | `IConversationSummarizer` | Claude Sonnet 4.6 |

### Lead Intelligence (4) — `Clawbot.Agents.Core.Skills.Lead.*`

| Skill | Agents | C# adapter | Source |
|---|---|---|---|
| Lead Deduplication | Lead | `ILeadDeduplicator` | Qdrant cosine |
| Contact Enrichment | Lead, SaleAssist | `IContactEnricher` | Hunter.io + Apollo.io |
| Timezone Detection | Lead, Chat | `ITimezoneDetector` | NodaTime |
| Spam Detection | Chat, Lead | `ISpamDetector` | Akismet |

### Content Marketing (6) — `Clawbot.Agents.Core.Skills.Content.*`

| Skill | Agents | C# adapter | Source |
|---|---|---|---|
| Hashtag Research VN | Research, Content | `IHashtagResearcher` | TikTok Creative Center + Google Trends |
| ZH Script Validation | Content, Chat | `IZhScriptValidator` | OpenCC |
| Image Prompt Generation | Content | `IImagePromptGenerator` | Claude + Replicate FLUX |
| Short Video Script Formula | Content | `IVideoScriptComposer` | Internal Hook/Value/CTA |
| VI ↔ ZH Translation | Chat, Content | `IViZhTranslator` | Claude + glossary KB |
| Competitor Monitor | Research | `ICompetitorMonitor` | RSS + scrape |

### Ops / Cross-cutting (6) — `Clawbot.Agents.Core.Skills.Ops.*`

| Skill | Agents | C# adapter | Source |
|---|---|---|---|
| PDF Table Renderer | Docs | `IPdfTableRenderer` | QuestPDF |
| QR Code Generator | Docs, Content | `IQrGenerator` | QRCoder |
| Anomaly Detection | Report, Ads | `IAnomalyDetector` | Math.NET z-score |
| 7-Day Forecast | Report | `IForecaster` | ML.NET TimeSeries SSA |
| Prompt Injection Defender | Chat, SaleAssist, Lead | `IPromptInjectionDefender` | Lakera Guard / llm-guard |
| Claude Cost Tracker | ALL 8 agents | `IClaudeCostTracker` | OTel gen_ai + SQLite |

## Agent → Skill matrix

| Agent | Phase 1 skills | Phase 2 skills |
|---|---|---|
| Agent-Chat | knowledge-base-tieng-trung, 50-chat-scenarios, zalo-sales-consultation, platform-specs | intent-classification, sentiment-analysis, language-detection, pii-redaction, toxicity-filter, conversation-summarization, spam-detection, timezone-detection, vi-zh-translation, prompt-injection-defender, claude-cost-tracker |
| Agent-SaleAssist | knowledge-base-tieng-trung, 50-chat-scenarios, zalo-sales-consultation | intent-classification, sentiment-analysis, language-detection, pii-redaction, toxicity-filter, conversation-summarization, contact-enrichment, prompt-injection-defender, claude-cost-tracker |
| Agent-Lead | lead-scoring | lead-deduplication, contact-enrichment, timezone-detection, spam-detection, prompt-injection-defender, claude-cost-tracker |
| Agent-Content | platform-specs, content-copywriting | hashtag-research-vn, zh-script-validation, image-prompt-generation, short-video-script-formula, vi-zh-translation, competitor-monitor, qr-code-generator, claude-cost-tracker |
| Agent-Docs | doc-templates | pdf-table-renderer, qr-code-generator, claude-cost-tracker |
| Agent-Ads | ads-optimization | anomaly-detection, claude-cost-tracker |
| Agent-Report | — | sentiment-analysis, pii-redaction, anomaly-detection, forecast-7day, claude-cost-tracker |
| Agent-Research | trend-tieng-trung | hashtag-research-vn, competitor-monitor, claude-cost-tracker |
