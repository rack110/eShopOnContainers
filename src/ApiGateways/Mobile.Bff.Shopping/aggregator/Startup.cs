﻿using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopOnContainers.Mobile.Shopping.HttpAggregator.Config;
using Microsoft.eShopOnContainers.Mobile.Shopping.HttpAggregator.Filters.Basket.API.Infrastructure.Filters;
using Microsoft.eShopOnContainers.Mobile.Shopping.HttpAggregator.Infrastructure;
using Microsoft.eShopOnContainers.Mobile.Shopping.HttpAggregator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pivotal.Discovery.Client;
using Polly;
using Polly.Extensions.Http;
using Steeltoe.Common.Http.Discovery;
using Steeltoe.Management.CloudFoundry;
using Swashbuckle.AspNetCore.Swagger;

namespace Microsoft.eShopOnContainers.Mobile.Shopping.HttpAggregator
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCloudFoundryActuators(Configuration);
            services.AddDiscoveryClient(Configuration);
            var identityServerUrl = services.GetExternalIdentityUrl();
            services.AddCustomMvc(Configuration, identityServerUrl)
                 .AddCustomAuthentication(Configuration, identityServerUrl)
                 .AddHttpServices(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            var pathBase = Configuration["PATH_BASE"];

            if (!string.IsNullOrEmpty(pathBase))
            {
                loggerFactory.CreateLogger("init").LogDebug($"Using PATH BASE '{pathBase}'");
                app.UsePathBase(pathBase);
            }

            app.UseCors("CorsPolicy");
            app.UseCloudFoundryActuators();
            app.UseDiscoveryClient();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseAuthentication();

            app.UseMvc();

            app.UseSwagger().UseSwaggerUI(c =>
           {
               c.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "Purchase BFF V1");

               c.OAuthClientId("MobileShoppingAgg");
               c.OAuthClientSecret(string.Empty);
               c.OAuthRealm(string.Empty);
               c.OAuthAppName("Purchase BFF Swagger UI");
           });
        }
    }

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCustomMvc(this IServiceCollection services, IConfiguration configuration, string identityServerUrl)
        {
            services.AddOptions();
            services.Configure<UrlsConfig>(configuration);

            services.AddMvc();

            services.AddSwaggerGen(options =>
            {
                options.DescribeAllEnumsAsStrings();
                options.SwaggerDoc("v1", new Swashbuckle.AspNetCore.Swagger.Info
                {
                    Title = "Shopping Aggregator for Mobile Clients",
                    Version = "v1",
                    Description = "Shopping Aggregator for Mobile Clients",
                    TermsOfService = "Terms Of Service"
                });

                options.AddSecurityDefinition("oauth2", new OAuth2Scheme
                {
                    Type = "oauth2",
                    Flow = "implicit",
                    AuthorizationUrl = $"{identityServerUrl}/connect/authorize",
                    TokenUrl = $"{identityServerUrl}/connect/token",
                    Scopes = new Dictionary<string, string>()
                    {
                        { "mobileshoppingagg", "Shopping Aggregator for Mobile Clients" }
                    }
                });

                options.OperationFilter<AuthorizeCheckOperationFilter>();
            });

            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
            });

            return services;
        }

        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration, string identityServerUrl)
        {
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            }).AddJwtBearer(options =>
            {
                options.Authority = identityServerUrl;
                options.RequireHttpsMetadata = false;
                options.Audience = "mobileshoppingagg";
                options.Events = new JwtBearerEvents()
                {
                    OnAuthenticationFailed = async ctx =>
                    {
                    },
                    OnTokenValidated = async ctx =>
                    {
                    }
                };
                options.SetBackChannelCertificateValidation(bool.Parse(configuration["validateCertificates"]));
            });

            return services;
        }

        public static IServiceCollection AddHttpServices(this IServiceCollection services, IConfiguration configuration)
        {
            //register delegating handlers
            services.AddTransient<HttpClientAuthorizationDelegatingHandler>();
            services.AddTransient<DiscoveryHttpMessageHandler>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            bool.TryParse(configuration["validateCertificates"], out bool validateCertificates);

            //register http services
            services.AddHttpClient<IBasketService, BasketService>()
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

            services.AddHttpClient<IOrderApiClient, OrderApiClient>()
                .SetCertificateValidation(validateCertificates)
                .AddServiceDiscovery()
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

            return services;
        }

        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
              .HandleTransientHttpError()
              .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
              .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
        }
    }
}
