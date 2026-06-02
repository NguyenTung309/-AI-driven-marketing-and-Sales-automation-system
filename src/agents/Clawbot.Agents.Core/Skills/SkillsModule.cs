using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Skills;

// DI registration for all 22 ClawBot skills.
// Stubs throw NotImplementedException — wire concrete impls per skill SPEC.
// See .sdd/skills/_index.md for the full catalog.
public static class SkillsModule
{
    public static IServiceCollection AddClawbotSkills(this IServiceCollection services)
    {
        // NLP
        services.AddSingleton<IIntentClassifier, KeywordIntentClassifier>();
        services.AddSingleton<ISentimentAnalyzer, LexiconSentimentAnalyzer>();
        services.AddSingleton<ILanguageDetector, FastTextLanguageDetector>();
        services.AddSingleton<IPiiRedactor, RegexPiiRedactor>();
        services.AddSingleton<IToxicityFilter, DetoxifyToxicityFilter>();
        services.AddSingleton<IConversationSummarizer, ClaudeConversationSummarizer>();

        // Lead
        services.AddSingleton<ILeadDeduplicator, QdrantLeadDeduplicator>();
        services.AddSingleton<IContactEnricher, HunterContactEnricher>();
        services.AddSingleton<ITimezoneDetector, NodaTimezoneDetector>();
        services.AddSingleton<ISpamDetector, AkismetSpamDetector>();

        // Content
        services.AddSingleton<IHashtagResearcher, TikTokHashtagResearcher>();
        services.AddSingleton<IZhScriptValidator, OpenCcZhScriptValidator>();
        services.AddSingleton<IImagePromptGenerator, ClaudeImagePromptGenerator>();
        services.AddSingleton<IVideoScriptComposer, HvcVideoScriptComposer>();
        services.AddSingleton<IViZhTranslator, ClaudeViZhTranslator>();
        services.AddSingleton<ICompetitorMonitor, RssCompetitorMonitor>();

        // Ops / Cross-cutting
        services.AddSingleton<IPdfTableRenderer, QuestPdfTableRenderer>();
        services.AddSingleton<IQrGenerator, QRCoderGenerator>();
        services.AddSingleton<IAnomalyDetector, ZScoreAnomalyDetector>();
        services.AddSingleton<IForecaster, MlNetForecaster>();
        services.AddSingleton<IPromptInjectionDefender, HeuristicPromptInjectionDefender>();
        services.AddSingleton<IClaudeCostTracker, InMemoryClaudeCostTracker>();

        return services;
    }
}
