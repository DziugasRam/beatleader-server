﻿using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using BeatLeader_Server.Services;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Prometheus.Client.DependencyInjection;
using Prometheus.Client.HttpRequestDurations;
using Prometheus.Client.AspNetCore;
using System.Net;
using BeatLeader_Server.Bot;

namespace BeatLeader_Server {

    public class ErrorLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorLoggingMiddleware> _logger;

        public ErrorLoggingMiddleware(RequestDelegate next, ILogger<ErrorLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception e)
            {
                _logger.LogWarning(null, "LOL500 " + context.Request.Path + context.Request.QueryString);
                throw;
            }
        }
    }

    public class LocalstatsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LocalstatsMiddleware> _logger;
 
        public LocalstatsMiddleware(RequestDelegate next, ILogger<LocalstatsMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }
 
        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path == "/metrics")
            {
                var remoteIp = context.Connection.RemoteIpAddress;
 
                if (context.Request.Headers.ContainsKey("X-Forwarded-For") || !IPAddress.IsLoopback(remoteIp))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }
            }
 
            await _next(context);
        }
    }

    public class Startup {
        static string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
        public Startup (IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            Environment = env;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Environment { get; }

        public void ConfigureServices (IServiceCollection services)
        {
            string steamKey = Configuration.GetValue<string>("SteamKey");
            string steamApi = Configuration.GetValue<string>("SteamApi");

            string beatSaverId = Configuration.GetValue<string>("BeatSaverId");
            string beatSaverSecret = Configuration.GetValue<string>("BeatSaverSecret");

            string? cookieDomain = Configuration.GetValue<string>("CookieDomain");

            services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(@"../keys/"))
            .SetApplicationName("/home/site/wwwroot/");
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });

            var authBuilder = services.AddAuthentication (options => {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })

            .AddCookie (options => {
                options.Events.OnRedirectToAccessDenied =
                options.Events.OnRedirectToLogin = c => {
                    c.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.FromResult<object> (null);
                };
                options.Cookie.SameSite = SameSiteMode.None;
                if (cookieDomain != null) {
                    options.Cookie.Domain = cookieDomain;
                }
                options.Cookie.HttpOnly = false;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.Cookie.MaxAge = options.ExpireTimeSpan;
                options.SlidingExpiration = true;
            })
            .AddCookie("BLPatreon")
            .AddCookie("BLTwitch")
            .AddCookie("BLTwitter")
            .AddCookie("BLGoogle")
            .AddCookie("BLDiscord")
            .AddCookie("BLBeatSaver")
            .AddSteamTicket(options =>
            {
                options.Key = steamKey;
                options.ApplicationID = "620980";
                options.ApiUrl = steamApi;
            })
            .AddOculusToken (options =>
            {
            })
            .AddOculus(options => {})
            .AddSteam (options => {
                options.ApplicationKey = steamKey;
                options.Events.OnAuthenticated = ctx => {
                    /* ... */
                    return Task.CompletedTask;
                };
            })
            .AddBeatSaver(options => {
                options.SignInScheme = "BLBeatSaver";
                options.SaveTokens = true;
                options.ClientId = beatSaverId;
                options.ClientSecret = beatSaverSecret;
            });

            services.AddMemoryCache();
            services.Configure<IpRateLimitOptions>(options =>
            {
                options.EnableEndpointRateLimiting = true;
                options.StackBlockedRequests = false;
                options.HttpStatusCode = 429;
                options.RealIpHeader = "X-Real-IP";
                options.ClientIdHeader = "X-ClientId";
                options.GeneralRules = new List<RateLimitRule>
                    {
                        new RateLimitRule
                        {
                            Endpoint = "GET:/score/*",
                            Period = "10s",
                            Limit = 10,
                        }
                    };
            });
            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
            services.AddInMemoryRateLimiting();

            if (!Environment.IsDevelopment()) {

                string patreonId = Configuration.GetValue<string>("PatreonId");
                string patreonSecret = Configuration.GetValue<string>("PatreonSecret");
                authBuilder.AddPatreon(options => {
                    options.SignInScheme = "BLPatreon";
                    options.SaveTokens = true;
                    options.ClientId = patreonId;
                    options.ClientSecret = patreonSecret;
                });

                string twitchId = Configuration.GetValue<string>("TwitchId");
                string twitchSecret = Configuration.GetValue<string>("TwitchSecret");
                authBuilder.AddTwitch(options =>
                {
                    options.SaveTokens = true;
                    options.ClientId = twitchId;
                    options.ClientSecret = twitchSecret;
                    options.SignInScheme = "BLTwitch";
                });

                string twitterId = Configuration.GetValue<string>("TwitterId");
                string twitterSecret = Configuration.GetValue<string>("TwitterSecret");
                authBuilder.AddTwitter(options =>
                {
                    options.SaveTokens = true;
                    options.ClientId = twitterId;
                    options.ClientSecret = twitterSecret;
                    options.SignInScheme = "BLTwitter";
                });

                string discordId = Configuration.GetValue<string>("DiscordId");
                string discordSecret = Configuration.GetValue<string>("DiscordSecret");
                authBuilder.AddDiscord(options => 
                {
                    options.SaveTokens = true;
                    options.ClientId = discordId;
                    options.ClientSecret = discordSecret;
                    options.SignInScheme = "BLDiscord";
                });

                string googleId = Configuration.GetValue<string>("GoogleId");
                string googleSecret = Configuration.GetValue<string>("GoogleSecret");
                authBuilder.AddGoogle(options =>
                {
                    options.SaveTokens = true;
                    options.ClientId = googleId;
                    options.ClientSecret = googleSecret;
                    options.SignInScheme = "BLGoogle";
                    options.Scope.Add("https://www.googleapis.com/auth/youtube.readonly");
                });
            }

            services.AddServerTiming();
            services.AddMetricFactory();

            services.AddDbContext<AppContext>(options => options.UseSqlServer(Configuration.GetValue<string>("DefaultConnection")));
            services.AddDbContext<ReadAppContext>(options => options.UseSqlServer(Configuration.GetValue<string>("ReadOnlyConnection")));

            if (Configuration.GetValue<string>("ServicesHost") == "YES")
            {
                services.AddHostedService<HourlyRefresh>();
                services.AddHostedService<DailyRefresh>();
                services.AddHostedService<HistoryService>();
                services.AddHostedService<RankingService>();
                services.AddHostedService<MinuteRefresh>();
            }
            services.AddHostedService<BotService>();
            services.AddSingleton<NominationsForum>();

            services.AddMvc ().AddControllersAsServices ().AddJsonOptions (options => {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
            services.AddCors (options => {
                options.AddPolicy (name: MyAllowSpecificOrigins,
                    builder => {
                        builder.WithOrigins(Configuration.GetSection("CORS").Get<string[]>())
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                    });
            });

            services.AddSwaggerGen();

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });

            RequestDecompressionServiceCollectionExtensions.AddRequestDecompression(services);
        }

        public void Configure (IApplicationBuilder app)
        {
            app.UseMiddleware<ErrorLoggingMiddleware>();
            app.UseMiddleware<LocalstatsMiddleware>();
            app.UsePrometheusServer();
            app.UsePrometheusRequestDurations();
            app.UseStaticFiles();
            app.UseForwardedHeaders();
            app.UseServerTiming();
            app.UseWebSockets(new WebSocketOptions {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });
            app.UseIpRateLimiting();

            app.UseRouting ();
            app.UseCookiePolicy(new CookiePolicyOptions {
                Secure = CookieSecurePolicy.Always
            });

            app.UseAuthentication ();
            app.UseAuthorization ();

            app.UseCors (MyAllowSpecificOrigins);

            app.UseResponseCompression();
            RequestDecompressionApplicationBuilderExtensions.UseRequestDecompression(app);

            app.UseEndpoints (endpoints => {
                endpoints.MapDefaultControllerRoute ();
            });

            app.UseSwagger();
            app.UseSwaggerUI();
        }
    }
}
