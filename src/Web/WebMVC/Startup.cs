﻿using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.ServiceFabric;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.eShopOnContainers.WebMVC.Services;
using Microsoft.eShopOnContainers.WebMVC.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.HealthChecks;
using Microsoft.Extensions.Logging;
using Pivotal.Discovery.Client;
using Pivotal.Discovery.Eureka;
using Polly;
using Polly.Extensions.Http;
using Steeltoe.CloudFoundry.Connector.Redis;
using Steeltoe.Common.Discovery;
using Steeltoe.Common.Http.Discovery;
using Steeltoe.Management.CloudFoundry;
using Steeltoe.Security.DataProtection;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using WebMVC.Infrastructure;
using WebMVC.Infrastructure.Middlewares;
using WebMVC.Services;

namespace Microsoft.eShopOnContainers.WebMVC
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the IoC container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAppInsight(Configuration)
                    .AddHealthChecks(Configuration)
                    .AddRedisConnectionMultiplexer(Configuration)
                    .AddDiscoveryClient(Configuration)
                    .AddCustomMvc(Configuration)
                    .AddHttpClientServices(Configuration)
                    //.AddHttpClientLogging(Configuration)  //Opt-in HttpClientLogging config
                    .AddCustomAuthentication(Configuration);
            services.AddCloudFoundryActuators(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            loggerFactory.AddAzureWebAppDiagnostics();
            loggerFactory.AddApplicationInsights(app.ApplicationServices, LogLevel.Trace);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedProto
            });

            var pathBase = Configuration["PATH_BASE"];
            if (!string.IsNullOrEmpty(pathBase))
            {
                loggerFactory.CreateLogger("init").LogDebug($"Using PATH BASE '{pathBase}'");
                app.UsePathBase(pathBase);
            }

            app.UseCloudFoundryActuators();
            app.UseDiscoveryClient();

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
            app.Map("/liveness", lapp => lapp.Run(async ctx => ctx.Response.StatusCode = 200));
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

            app.UseSession();
            app.UseStaticFiles();

            if (Configuration.GetValue<bool>("UseLoadTest"))
            {
                app.UseMiddleware<ByPassAuthMiddleware>();
            }

            app.UseAuthentication();

            var log = loggerFactory.CreateLogger("identity");

            WebContextSeed.Seed(app, env, loggerFactory);

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Catalog}/{action=Index}/{id?}");

                routes.MapRoute(
                    name: "defaultError",
                    template: "{controller=Error}/{action=Error}");
            });
        }
    }

    static class ServiceCollectionExtensions
    {

        public static IServiceCollection AddAppInsight(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddApplicationInsightsTelemetry(configuration);
            var orchestratorType = configuration.GetValue<string>("OrchestratorType");

            if (orchestratorType?.ToUpper() == "K8S")
            {
                // Enable K8s telemetry initializer
                services.EnableKubernetes();
            }

            if (orchestratorType?.ToUpper() == "SF")
            {
                // Enable SF telemetry initializer
                services.AddSingleton<ITelemetryInitializer>((serviceProvider) =>
                    new FabricTelemetryInitializer());
            }

            return services;
        }

        public static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHealthChecks(checks =>
            {
                var minutes = 1;
                if (int.TryParse(configuration["HealthCheck:Timeout"], out var minutesParsed))
                {
                    minutes = minutesParsed;
                }

                var healthPath = "/management/health";
                checks.AddUrlCheck(configuration["CatalogUrl"] + healthPath, TimeSpan.FromMinutes(minutes));
                checks.AddUrlCheck(configuration["OrderingUrl"] + healthPath, TimeSpan.FromMinutes(minutes));
                checks.AddUrlCheck(configuration["BasketUrl"] + healthPath, TimeSpan.Zero); //No cache for this HealthCheck, better just for demos 
                checks.AddUrlCheck(configuration["IdentityUrl"] + healthPath, TimeSpan.FromMinutes(minutes));
                checks.AddUrlCheck(configuration["MarketingUrl"] + healthPath, TimeSpan.FromMinutes(minutes));
            });

            return services;
        }

        public static IServiceCollection AddCustomMvc(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions();
            services.Configure<AppSettings>(configuration);

            services.PostConfigure<AppSettings>(settings =>
            {
                // when deployed to cloud foundry, use the external url of the signalr hub
                if (Steeltoe.Common.Platform.IsCloudFoundry)
                {
                    var sp = services.BuildServiceProvider();
                    var discoverer = sp.GetService<IDiscoveryClient>();
                    var gatewayUrl = discoverer.GetExternalUrlForApplication("webshoppingapigw", sp.GetService<ILogger<IDiscoveryClient>>());
                    settings.SignalrHubUrl = settings.SignalrHubUrl.Replace("http://:5202", gatewayUrl);
                }
            });

            services.AddMvc();

            services.AddSession();

            if (configuration.GetValue<string>("IsClusterEnv") == bool.TrueString)
            {
                services.AddDataProtection(opts =>
                {
                    opts.ApplicationDiscriminator = "eshop.webmvc";
                })
                .PersistKeysToRedis()
                .SetApplicationName("DataProtection-Keys");
            }
            return services;
        }

        // Adds all Http client services (like Service-Agents) using resilient Http requests based on HttpClient factory and Polly's policies 
        public static IServiceCollection AddHttpClientServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            //register delegating handlers
            services.AddTransient<HttpClientAuthorizationDelegatingHandler>();
            services.AddTransient<HttpClientRequestIdDelegatingHandler>();
            services.AddTransient<DiscoveryHttpMessageHandler>();

            //set 5 min as the lifetime for each HttpMessageHandler int the pool
            services.AddHttpClient("extendedhandlerlifetime").SetHandlerLifetime(TimeSpan.FromMinutes(5));

            bool.TryParse(configuration["validateCertificates"], out bool validateCertificates);

            //add http client services
            services.AddHttpClient<IBasketService, BasketService>()
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))  //Sample. Default lifetime is 2 minutes
                .SetCertificateValidation(validateCertificates)
                .AddHttpMessageHandler<HttpClientAuthorizationDelegatingHandler>()
                .AddServiceDiscovery()
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            services.AddHttpClient<ICatalogService, CatalogService>()
                .SetCertificateValidation(validateCertificates)
                .AddServiceDiscovery()
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            services.AddHttpClient<IOrderingService, OrderingService>()
                .SetCertificateValidation(validateCertificates)
                .AddHttpMessageHandler<HttpClientAuthorizationDelegatingHandler>()
                .AddHttpMessageHandler<HttpClientRequestIdDelegatingHandler>()
                .AddServiceDiscovery()
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            services.AddHttpClient<ICampaignService, CampaignService>()
                .SetCertificateValidation(validateCertificates)
                .AddHttpMessageHandler<HttpClientAuthorizationDelegatingHandler>()
                .AddServiceDiscovery()
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            services.AddHttpClient<ILocationService, LocationService>()
                .SetCertificateValidation(validateCertificates)
                .AddHttpMessageHandler<HttpClientAuthorizationDelegatingHandler>()
                .AddServiceDiscovery()
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            //add custom application services
            services.AddTransient<IIdentityParser<ApplicationUser>, IdentityParser>();

            return services;
        }

        public static IServiceCollection AddHttpClientLogging(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddLogging(b =>
            {
                b.AddFilter((category, level) => true); // Spam the world with logs.

                // Add console logger so we can see all the logging produced by the client by default.
                b.AddConsole(c => c.IncludeScopes = true);

                // Add console logger
                b.AddDebug();
            });

            return services;
        }

        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var identityUrl = services.GetExternalIdentityUrl();
            Console.WriteLine("Using {0} as the OpenIdConnect Authority", identityUrl);
            var useLoadTest = configuration.GetValue<bool>("UseLoadTest");
            var callBackUrl = configuration.GetValue<string>("eureka:instance:metadataMap:externalUrl");
            if (!callBackUrl.StartsWith("http"))
            {
                callBackUrl = "https://" + callBackUrl;
            }
            
            // Add Authentication services          
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(setup => setup.ExpireTimeSpan = TimeSpan.FromHours(2))
            .AddOpenIdConnect(options =>
            {
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.Authority = identityUrl;
                options.SignedOutRedirectUri = callBackUrl.ToString();
                options.ClientId = useLoadTest ? "mvctest" : "WebMvc";
                options.ClientSecret = "secret";
                options.ResponseType = useLoadTest ? "code id_token token" : "code id_token";
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.RequireHttpsMetadata = false;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("orders");
                options.Scope.Add("basket");
                options.Scope.Add("marketing");
                options.Scope.Add("locations");
                options.Scope.Add("webshoppingagg");
                options.Scope.Add("orders.signalrhub");
                options.SetBackChannelCertificateValidation(bool.Parse(configuration["validateCertificates"]));
            });

            return services;
        }

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
              .HandleTransientHttpError()
              .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
              .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        }

        static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
        }
    }
}
