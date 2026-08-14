using Clawbot.Agents.Contracts.Chat;
using Clawbot.Infrastructure.Security;
using FluentAssertions;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Tests;

public sealed class AgentServiceGrpcClientRegistrationTests
{
    [Fact]
    public void AddInfrastructure_RegistersChatClientAuthenticationInterceptor()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentService:Url"] = "http://localhost:15875",
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=unused;",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@localhost:5672",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
        var registrations = options
            .Get(typeof(ChatAgent.ChatAgentClient).Name)
            .InterceptorRegistrations;

        // Assert
        registrations
            .Select(registration => registration.Creator(provider))
            .Should().Contain(interceptor => interceptor is AgentServiceClientAuthInterceptor);
    }
}
