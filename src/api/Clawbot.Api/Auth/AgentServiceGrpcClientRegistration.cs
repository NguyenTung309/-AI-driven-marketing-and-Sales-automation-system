using Clawbot.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Auth;

internal static class AgentServiceGrpcClientRegistration
{
    internal static IServiceCollection AddApiAgentServiceGrpcClients(
        this IServiceCollection services,
        Uri agentServiceUrl,
        AgentServiceGrpcHandlerFactory handlerFactory)
    {
        services
            .AddGrpcClient<Clawbot.Agents.Contracts.SaleAssist.SaleAssistAgent.SaleAssistAgentClient>(options =>
            {
                options.Address = agentServiceUrl;
            })
            .ConfigurePrimaryHttpMessageHandler(_ => handlerFactory.Create())
            .AddInterceptor<AgentServiceClientAuthInterceptor>();

        services
            .AddGrpcClient<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>(options =>
            {
                options.Address = agentServiceUrl;
            })
            .ConfigurePrimaryHttpMessageHandler(_ => handlerFactory.Create())
            .AddInterceptor<AgentServiceClientAuthInterceptor>();

        services
            .AddGrpcClient<Clawbot.Agents.Contracts.Content.ContentAgent.ContentAgentClient>(options =>
            {
                options.Address = agentServiceUrl;
            })
            .ConfigurePrimaryHttpMessageHandler(_ => handlerFactory.Create())
            .AddInterceptor<AgentServiceClientAuthInterceptor>();

        services
            .AddGrpcClient<Clawbot.Agents.Contracts.Research.ResearchAgent.ResearchAgentClient>(options =>
            {
                options.Address = agentServiceUrl;
            })
            .ConfigurePrimaryHttpMessageHandler(_ => handlerFactory.Create())
            .AddInterceptor<AgentServiceClientAuthInterceptor>();

        services
            .AddGrpcClient<Clawbot.Agents.Contracts.Lead.LeadAgent.LeadAgentClient>(options =>
            {
                options.Address = agentServiceUrl;
            })
            .ConfigurePrimaryHttpMessageHandler(_ => handlerFactory.Create())
            .AddInterceptor<AgentServiceClientAuthInterceptor>();

        services
            .AddGrpcClient<Clawbot.Agents.Contracts.Report.ReportAgent.ReportAgentClient>(options =>
            {
                options.Address = agentServiceUrl;
            })
            .ConfigurePrimaryHttpMessageHandler(_ => handlerFactory.Create())
            .AddInterceptor<AgentServiceClientAuthInterceptor>();

        services
            .AddGrpcClient<Clawbot.Agents.Contracts.Orchestrator.Orchestrator.OrchestratorClient>(options =>
            {
                options.Address = agentServiceUrl;
            })
            .ConfigurePrimaryHttpMessageHandler(_ => handlerFactory.Create())
            .AddInterceptor<OrchestratorServiceAuthInterceptor>();

        return services;
    }
}
