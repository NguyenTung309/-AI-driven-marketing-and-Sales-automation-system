using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clawbot.Agents.Core.Skills;

// DI registration for all 22 ClawBot skills.
// Registers the default concrete adapters; external integrations remain config-gated.
// See .sdd/skills/_index.md for the full catalog.
public static class SkillsModule
{
    public static IServiceCollection AddClawbotSkills(this IServiceCollection services, IConfiguration cfg)
    {
        // Options binding
        services.Configure<SummarizerOptions>(cfg.GetSection(SummarizerOptions.SectionName));
        services.Configure<ToxicityOptions>(cfg.GetSection(ToxicityOptions.SectionName));
        services.Configure<ContactEnricherOptions>(cfg.GetSection(ContactEnricherOptions.SectionName));

        // NLP
        services.AddSingleton<IIntentClassifier, KeywordIntentClassifier>();
        services.AddSingleton<ISentimentAnalyzer, LexiconSentimentAnalyzer>();
        services.AddSingleton<ILanguageDetector, FastTextLanguageDetector>();
        services.AddSingleton<IPiiRedactor, RegexPiiRedactor>();
        services.AddSingleton<IToxicityFilter, DetoxifyToxicityFilter>();
        services.AddSingleton<IConversationSummarizer, ClaudeConversationSummarizer>();

        // Lead
        // Scoped (not singleton like the other skills): depends on IEmbeddingProvider, which
        // captures the scoped DbContext-backed IEmbeddingConfigResolver. Singleton = captive dep.
        services.AddScoped<ILeadDeduplicator, QdrantLeadDeduplicator>();
        services.AddSingleton<IContactEnricher, HunterContactEnricher>();
        services.AddSingleton<ITimezoneDetector, NodaTimezoneDetector>();
        services.AddSingleton<ISpamDetector, AkismetSpamDetector>();

        // Content
        services.AddSingleton<IHashtagResearcher, TikTokHashtagResearcher>();
        services.AddSingleton<IZhScriptValidator, OpenCcZhScriptValidator>();
        services.AddSingleton<IImagePromptGenerator, ClaudeImagePromptGenerator>();
        services.AddSingleton<IVideoScriptComposer, HvcVideoScriptComposer>();
        services.AddSingleton<IViZhTranslator, ClaudeViZhTranslator>();
        // ICompetitorMonitor is registered in AddInfrastructure (AddCompetitorMonitor) so the API's
        // Hangfire CompetitorScanJob can resolve it without pulling in the full skills module.

        // Ops / Cross-cutting
        services.AddSingleton<IPdfTableRenderer, QuestPdfTableRenderer>();
        services.AddSingleton<IQrGenerator, QRCoderGenerator>();
        services.AddSingleton<IAnomalyDetector, ZScoreAnomalyDetector>();
        services.AddSingleton<IForecaster, MlNetForecaster>();
        services.AddSingleton<IPromptInjectionDefender, HeuristicPromptInjectionDefender>();
        services.AddSingleton<IClaudeCostTracker, InMemoryClaudeCostTracker>();
        services.AddSingleton<Chat.IAgentToggleGate, Chat.AlwaysEnabledAgentToggleGate>();

        // HttpClient for contact enricher (config-gated Hunter/Apollo)
        services.AddHttpClient(nameof(HunterContactEnricher), c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }

    /// <summary>
    /// Registers only the PII redactor. Hosts that need PII redaction (e.g. the API's
    /// audit interceptor) but not the full agent skill catalog can call this instead of
    /// <see cref="AddClawbotSkills"/>. <c>RegexPiiRedactor</c> is internal, so this is the
    /// only way for other assemblies to wire it.
    /// </summary>
    public static IServiceCollection AddClawbotPiiRedactor(this IServiceCollection services)
    {
        services.AddSingleton<IPiiRedactor, RegexPiiRedactor>();
        return services;
    }

    public static IServiceCollection AddClawbotForecasting(this IServiceCollection services)
    {
        services.TryAddSingleton<IForecaster, MlNetForecaster>();
        return services;
    }

    public static IServiceCollection AddClawbotChatSupport(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<ToxicityOptions>(cfg.GetSection(ToxicityOptions.SectionName));
        services.TryAddSingleton<IIntentClassifier, KeywordIntentClassifier>();
        services.TryAddSingleton<IPiiRedactor, RegexPiiRedactor>();
        services.TryAddSingleton<ILanguageDetector, FastTextLanguageDetector>();
        services.TryAddSingleton<IToxicityFilter, DetoxifyToxicityFilter>();
        services.TryAddSingleton<ISpamDetector, AkismetSpamDetector>();
        services.TryAddSingleton<IPromptInjectionDefender, HeuristicPromptInjectionDefender>();
        services.TryAddSingleton<IClaudeCostTracker, InMemoryClaudeCostTracker>();
        services.TryAddSingleton<Chat.IAgentToggleGate, Chat.AlwaysEnabledAgentToggleGate>();
        return services;
    }
}
