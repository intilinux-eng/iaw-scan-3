using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IES_2.Coupe.Diagnostics;
using IES_2.Coupe.Logging;
using IES_2.Session;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace IES_2.Coupe.Web
{
    /// <summary>
    /// HTTP API used by wwwroot/app.js. Reads are plain GETs plus one Server-Sent Events stream;
    /// commands are POSTs that return at once (204, or 409 with a message), their outcome
    /// arriving later on the event stream.
    /// </summary>
    public static class ApiEndpoints
    {
        /// <summary>
        /// Required on every state-changing request. A custom header can't be sent cross-origin
        /// without a CORS preflight this server never approves, so a web page the phone opens
        /// elsewhere can't trigger an actuator test or wipe the logbook behind its back.
        /// </summary>
        public const string CommandHeader = "X-IAW-Coupe";

        public sealed record ConnectRequest(string? Ecu, string? Port, bool Simulation);
        public sealed record CheckedRequest(bool Checked);
        public sealed record ScreenRequest(string Screen);
        public sealed record RecordingRequest(int[] Indices);
        public sealed record ClockRequest(long UtcMs, string? TimeZone);
        public sealed record AutoLogRequest(bool Enabled);

        public static void MapCoupeApi(this WebApplication app)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api")
                    && !HttpMethods.IsGet(context.Request.Method)
                    && !context.Request.Headers.ContainsKey(CommandHeader))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next(context);
            });

            var api = app.MapGroup("/api");

            api.MapGet("/setup", (SettingsStore settings) => new SetupDto(
                CoupeCatalog.Ecus.Select(e => new EcuChoiceDto(
                    e.Option.Id,
                    e.Option.DisplayName,
                    e.Option.Badge,
                    e.Role,
                    e.Option.VehicleGroups.SelectMany(g => g.Vehicles).ToList())).ToList(),
                CoupeDiagnostics.ListPorts(),
                settings.LastEcu,
                settings.LastPort,
                About()));

            api.MapGet("/ports", () => CoupeDiagnostics.ListPorts());
            api.MapGet("/state", (CoupeDiagnostics diag) => diag.Snapshot());
            api.MapGet("/events", (EventHub hub, CoupeDiagnostics diag, CancellationToken ct) =>
                TypedResults.ServerSentEvents(hub.Stream(diag.Snapshot, ct)));

            api.MapPost("/clock", (ClockRequest request, DeviceClock clock, CoupeDiagnostics diag) =>
            {
                if (clock.SyncFromPhone(request.UtcMs, request.TimeZone) is { } sync)
                    diag.OnClockSynced(sync);
                return Results.NoContent();
            });

            api.MapPost("/connect", (ConnectRequest request, CoupeDiagnostics diag) =>
                Outcome(diag.BeginConnect(request.Ecu, request.Port, request.Simulation)));
            api.MapPost("/connect/cancel", (CoupeDiagnostics diag) =>
            {
                diag.CancelConnect();
                return Results.NoContent();
            });
            api.MapPost("/disconnect", (CoupeDiagnostics diag) =>
            {
                diag.Disconnect();
                return Results.NoContent();
            });
            api.MapPost("/reconnect", (CoupeDiagnostics diag) => Outcome(diag.Reconnect()));

            api.MapPost("/screen", (ScreenRequest request, CoupeDiagnostics diag) =>
            {
                diag.SetScreen(request.Screen);
                return Results.NoContent();
            });

            api.MapPost("/parameters", (CheckedRequest request, CoupeDiagnostics diag) =>
                Outcome(diag.SetAllParametersChecked(request.Checked)));
            api.MapPost("/parameters/{index:int}", (int index, CheckedRequest request, CoupeDiagnostics diag) =>
                Outcome(diag.SetParameterChecked(index, request.Checked)));

            api.MapPost("/errors/clear", (CoupeDiagnostics diag) => Outcome(diag.BeginClearCodes()));

            api.MapPost("/tests/{index:int}/run", (int index, CoupeDiagnostics diag) => Outcome(diag.BeginTest(index)));
            api.MapPost("/tests/cancel", (CoupeDiagnostics diag) =>
            {
                diag.CancelTest();
                return Results.NoContent();
            });

            api.MapPost("/recording/start", (RecordingRequest request, CoupeDiagnostics diag) =>
            {
                var (started, message) = diag.StartRecording(request.Indices);
                if (!started) return Results.Conflict(new { message });
                return Results.Ok(new { message });
            });
            api.MapPost("/recording/stop", (CoupeDiagnostics diag) =>
            {
                diag.StopRecording();
                return Results.NoContent();
            });

            api.MapGet("/recordings", () => Recorder.ListFiles());
            api.MapGet("/recordings/{name}", (string name) =>
                Recorder.ResolveFile(name) is { } path
                    ? Results.File(path, "text/csv", name)
                    : Results.NotFound());
            api.MapDelete("/recordings/{name}", (string name, CoupeDiagnostics diag) =>
            {
                if (Recorder.ResolveFile(name) is not { } path) return Results.NotFound();
                if (diag.IsRecordingFile(name)) return Results.Conflict(new { message = "Registrazione in corso su questo file." });
                return Delete(() => File.Delete(path));
            });

            MapDatalog(api);
        }

        private static void MapDatalog(RouteGroupBuilder api)
        {
            api.MapGet("/datalog", (DataLogger logger, DeviceClock clock) => new DatalogDto(
                logger.BootSeq,
                logger.UsedBytes(),
                logger.MaxBytes,
                Math.Round(1 / logger.DataIntervalSeconds, 1),
                DatalogArchive.BootDirectories()
                    .OrderByDescending(b => b.Seq)
                    .Select(b => DatalogArchive.Summarize(b, logger.BootSeq, clock.Now))
                    .ToList()));

            // ?boot=N for one power-on, ?boot=all for the whole logbook.
            api.MapGet("/datalog/export", (HttpContext context, string? boot, DataLogger logger, DeviceClock clock) =>
            {
                IReadOnlyList<DatalogArchive.BootDirectory> boots;
                string fileName;
                if (boot is null or "all")
                {
                    boots = DatalogArchive.BootDirectories();
                    fileName = "iaw-coupe-registro-completo.zip";
                }
                else if (int.TryParse(boot, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) && DatalogArchive.Find(seq) is { } one)
                {
                    boots = new[] { one };
                    fileName = $"iaw-coupe-accensione-{seq:D5}.zip";
                }
                else
                {
                    return Results.NotFound();
                }
                if (boots.Count == 0) return Results.NotFound();

                logger.Sync();
                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
                // ZipArchive writes synchronously; streaming it straight out avoids staging a possibly
                // large archive in RAM or on the eMMC first.
                context.Features.Get<IHttpBodyControlFeature>()!.AllowSynchronousIO = true;
                return Results.Stream(stream =>
                {
                    DatalogArchive.WriteZip(stream, boots, logger.BootSeq, clock.Now, version);
                    return Task.CompletedTask;
                }, "application/zip", fileName);
            });

            api.MapDelete("/datalog/{boot:int}", (int boot, DataLogger logger) =>
            {
                if (boot == logger.BootSeq) return Results.Conflict(new { message = "Non si può eliminare il registro dell'accensione in corso." });
                if (DatalogArchive.Find(boot) is not { } dir) return Results.NotFound();
                return Delete(() => Directory.Delete(dir.Path, recursive: true));
            });

            api.MapPost("/datalog/auto", (AutoLogRequest request, CoupeDiagnostics diag) =>
            {
                diag.SetAutoLog(request.Enabled);
                return Results.NoContent();
            });
            api.MapPost("/datalog/pause", (CoupeDiagnostics diag) =>
            {
                diag.PauseAuto();
                return Results.NoContent();
            });
            api.MapPost("/datalog/resume", (CoupeDiagnostics diag) =>
            {
                diag.ResumeAuto();
                return Results.NoContent();
            });
        }

        private static IResult Outcome(string? error) =>
            error == null ? Results.NoContent() : Results.Conflict(new { message = error });

        private static IResult Delete(Action delete)
        {
            try
            {
                delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.Conflict(new { message = "Impossibile eliminare: " + ex.Message });
            }
            return Results.NoContent();
        }

        private static AboutDto About()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "IAW Scan 3 Coupé";
            var about = AboutContent.Italian;
            return new AboutDto(product, "Versione " + assembly.GetName().Version, about.ForkCopyright, AboutContent.ForkRepoUrl,
                about.OriginalProjectLine, AboutContent.OriginalProjectUrl, about.Description, about.LicenseText);
        }
    }
}
