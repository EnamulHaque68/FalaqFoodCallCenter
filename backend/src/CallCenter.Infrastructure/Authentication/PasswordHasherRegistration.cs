using CallCenter.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Infrastructure.Authentication;

public static class PasswordHasherRegistration
{
    public static IServiceCollection AddPasswordHashing(
        this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        return services;
    }
}
