using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Finora.Infrastructure.Persistence;
using Finora.Infrastructure.Repositories;
using Finora.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Finora.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));
        services.Configure<PostmarkOptions>(configuration.GetSection(PostmarkOptions.SectionName));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.Configure<MarketDataOptions>(configuration.GetSection(MarketDataOptions.SectionName));
        services.Configure<LogoDevOptions>(configuration.GetSection(LogoDevOptions.SectionName));

        services.AddHttpClient<IEmailService, PostmarkEmailService>(client =>
        {
            client.BaseAddress = new Uri("https://api.postmarkapp.com/");
        });

        // Market data (Twelve Data + Yahoo) e câmbio (frankfurter). Yahoo exige User-Agent de browser.
        services.AddHttpClient<IMarketDataProvider, Finora.Infrastructure.Services.MarketData.MarketDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) FinoraFlow/1.0");
        });
        services.AddHttpClient<IFxRateService, FxRateService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IHouseholdService, HouseholdService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISavingsObjectiveService, SavingsObjectiveService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IStripeService, StripeService>();
        services.AddScoped<IRecurringAccountBalanceService, RecurringAccountBalanceService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IInvestmentService, InvestmentService>();
        services.AddScoped<IMarketDataRefreshService, MarketDataRefreshService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<ISavingsObjectiveRepository, SavingsObjectiveRepository>();
        services.AddScoped<IRecurringTransactionRepository, RecurringTransactionRepository>();
        services.AddScoped<IHouseholdRepository, HouseholdRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IInvestmentRepository, InvestmentRepository>();
        services.AddScoped<IInstrumentQuoteRepository, InstrumentQuoteRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IMonthlyReportRepository, MonthlyReportRepository>();
        services.AddScoped<IMonthlyReportGenerationService, MonthlyReportGenerationService>();
        services.AddHttpClient<IFileStorageService, SupabaseStorageService>();
        services.AddScoped<ICoupleInvitationRepository, CoupleInvitationRepository>();
        services.AddScoped<ICoupleInvitationService, CoupleInvitationService>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationGenerationService, NotificationGenerationService>();
        return services;
    }

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Finora")
            ?? throw new InvalidOperationException("Connection string 'Finora' not found.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration not found.");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep JWT claim types as in the token (e.g. "sub"); avoids missing NameIdentifier after inbound mapping.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = ClaimTypes.Role
                };
            });

        return services;
    }

    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Finora API",
                Version = "v1",
                Description = "Finora API with JWT Authentication"
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                In = ParameterLocation.Header,
                Description = "JWT Authorization header. Example: \"Bearer {token}\""
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
