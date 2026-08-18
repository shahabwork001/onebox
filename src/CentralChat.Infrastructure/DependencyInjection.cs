using CentralChat.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CentralChat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Section));
        services.Configure<MetaWhatsAppOptions>(configuration.GetSection(MetaWhatsAppOptions.Section));
        services.AddDbContext<CentralChatDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("PostgreSql")));
        services.AddIdentityCore<ApplicationUser>(o => { o.Password.RequiredLength = 10; o.Password.RequireDigit = true; o.Password.RequireUppercase = true; o.User.RequireUniqueEmail = true; })
            .AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<CentralChatDbContext>().AddDefaultTokenProviders();
        services.AddScoped<IAuthService, AuthService>(); services.AddScoped<ITicketService, TicketService>(); services.AddScoped<IConversationService, ConversationService>(); services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();
        services.AddScoped<IDirectoryService, DirectoryService>();
        services.AddSingleton<RabbitConnection>(); services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();
        var meta = configuration.GetSection(MetaWhatsAppOptions.Section).Get<MetaWhatsAppOptions>() ?? new();
        if (meta.UseDevelopmentClient) services.AddSingleton<IWhatsAppClient, DevelopmentWhatsAppClient>();
        else services.AddHttpClient<IWhatsAppClient, MetaWhatsAppClient>(x => x.BaseAddress = new Uri("https://graph.facebook.com/"));
        services.AddHostedService<OutboxPublisher>(); services.AddHostedService<RabbitConsumer>();
        return services;
    }
}
