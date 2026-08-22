using Clawbot.Application.Common.Behaviors;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        services.AddApplication().Should().BeSameAs(services);
    }

    [Fact]
    public void AddApplication_RegistersMediatorAndHandlers()
    {
        var services = new ServiceCollection().AddApplication();

        services.Should().Contain(d => d.ServiceType == typeof(IMediator));
        services.Should().Contain(d =>
            d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
    }

    [Fact]
    public void AddApplication_RegistersAllThreePipelineBehaviors()
    {
        var services = new ServiceCollection().AddApplication();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        behaviors.Should().Contain(typeof(LoggingBehavior<,>));
        behaviors.Should().Contain(typeof(ValidationBehavior<,>));
        behaviors.Should().Contain(typeof(AuditBehavior<,>));
    }

    [Fact]
    public void AddApplication_BehaviorsAreTransient()
    {
        var services = new ServiceCollection().AddApplication();

        services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void AddApplication_BehaviorOrder_LoggingThenValidationThenAudit()
    {
        // Thứ tự đăng ký quyết định thứ tự chạy pipeline: log trước, validate, rồi audit.
        var services = new ServiceCollection().AddApplication();

        services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .Should().Equal(
                typeof(LoggingBehavior<,>),
                typeof(ValidationBehavior<,>),
                typeof(AuditBehavior<,>));
    }
}
