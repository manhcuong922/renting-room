using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using renting_room.Application.Common.Behaviors;
using renting_room.Application.Common.Interfaces;
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
        services.AddScoped<Renters.RenterAnonymizer>();
        services.AddScoped<Payments.PaymentPosting>();
        services.AddScoped<IDomainEventHandler, Billing.DraftStalenessHandler>();
        services.AddScoped<Billing.RoomChargeBilling>();
        services.AddScoped<Billing.InvoiceInputLoader>();
        services.AddScoped<Billing.InvoiceFinalizer>();
        services.AddScoped<Billing.DraftRecalculator>();
        services.AddScoped<Billing.MeterDraftRefresher>();
        services.AddScoped<Billing.ReadingAdjustmentCharges>();
        services.AddScoped<Payments.WriteOffReduction>();
        services.AddScoped<Meters.ContractMeterReadings>();
        services.AddScoped<Contracts.ContractMutation>();
        services.AddScoped<Contracts.OccupantChecks>();
        services.AddScoped<Contracts.ResignVehicleRestorer>();
        services.AddScoped<Contracts.ContractPaperWarnings>();
        services.AddScoped<Contracts.ContractDraftBuilder>();

        return services;
    }
}
