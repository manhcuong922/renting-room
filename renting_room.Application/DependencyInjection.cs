using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using renting_room.Application.Common.Behaviors;
using renting_room.Application.Identity.Auth;

namespace renting_room.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Scoped (không phải Singleton): behavior phụ thuộc validator scoped — tránh captive dependency.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuthTokenIssuer>();

        return services;
    }
}
