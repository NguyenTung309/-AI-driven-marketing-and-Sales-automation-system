using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Lead;

public static class LeadModule
{
    public static IServiceCollection AddClawbotLead(this IServiceCollection services)
    {
        services.AddScoped<ILeadAssignmentService, LeastBusyLeadAssignmentService>();
        return services;
    }
}
