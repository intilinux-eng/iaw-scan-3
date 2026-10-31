using System;
using System.Globalization;
using IES_2.Coupe.Diagnostics;
using IES_2.Coupe.Logging;
using IES_2.Coupe.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IES_2.Coupe
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            // Parameter names, units and fault descriptions come from IES_3.Core's resources: show
            // them in Italian like the rest of this UI, whatever locale the board is set to.
            var italian = CultureInfo.GetCultureInfo("it-IT");
            CultureInfo.DefaultThreadCurrentCulture = italian;
            CultureInfo.DefaultThreadCurrentUICulture = italian;
            CultureInfo.CurrentCulture = italian;
            CultureInfo.CurrentUICulture = italian;

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                // The web UI ships next to the executable; the working directory (set by the
                // systemd unit) holds settings.json, the datalog/ logbook and Log/ recordings.
                ContentRootPath = AppContext.BaseDirectory,
            });

            var config = builder.Configuration;
            var clock = new DeviceClock();
            builder.Services.AddSingleton(clock);
            builder.Services.AddSingleton(_ => new DataLogger(
                clock,
                config.GetValue("IAW_DATALOG_MAX_MB", 2048L) * 1024 * 1024,
                config.GetValue("IAW_DATALOG_INTERVAL_MS", 200)));
            builder.Services.AddSingleton<EventHub>();
            builder.Services.AddSingleton<SettingsStore>();
            builder.Services.AddSingleton<CoupeDiagnostics>();
            builder.Services.AddHostedService<AutoLogger>();

            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                // After a package update the phone must pick up the new UI, not a cached one.
                OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
            });
            app.MapCoupeApi();

            app.Run();
        }
    }
}
