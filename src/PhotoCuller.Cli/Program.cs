using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoCuller.Application.Analysis;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Configuration;
using PhotoCuller.Infrastructure.Database;

namespace PhotoCuller.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> Main(string[] args)
    {
        if (!TryParseArguments(args, out var directory, out var outputPath))
            return args.Length == 1 && IsHelp(args[0]) ? 0 : 2;

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var cliDataDirectory = Path.Combine(
            Path.GetTempPath(),
            "PhotoCuller",
            "cli",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cliDataDirectory);
        var databasePath = Path.Combine(cliDataDirectory, "photoculler.db");

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddOptions<AppSettings>();
            builder.Services.AddInfrastructure(databasePath);
            builder.Services.AddTransient<IPhotoAnalysisEngine, PhotoAnalysisEngine>();

            using var host = builder.Build();
            await host.StartAsync(cancellation.Token);

            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<DatabaseInitializer>()
                .InitializeAsync(cancellation.Token);

            var settings = await scope.ServiceProvider
                .GetRequiredService<ISettingsRepository>()
                .LoadAsync(cancellation.Token);
            var engine = scope.ServiceProvider
                .GetRequiredService<IPhotoAnalysisEngine>();
            var run = await engine.AnalyzeAsync(
                directory!,
                PhotoAnalysisOptions.FromSettings(settings),
                cancellationToken: cancellation.Token);

            var json = JsonSerializer.Serialize(run.Report, JsonOptions);
            if (outputPath is null)
            {
                Console.Out.WriteLine(json);
            }
            else
            {
                var fullOutputPath = Path.GetFullPath(outputPath);
                var parent = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                await File.WriteAllTextAsync(fullOutputPath, json, cancellation.Token);
            }

            await host.StopAsync(CancellationToken.None);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Analysis cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Analysis failed: {exception.Message}");
            return 1;
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
            try
            {
                Directory.Delete(cliDataDirectory);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool TryParseArguments(
        IReadOnlyList<string> args,
        out string? directory,
        out string? outputPath)
    {
        directory = null;
        outputPath = null;

        if (args.Count == 1 && IsHelp(args[0]))
        {
            PrintUsage(Console.Out);
            return false;
        }

        if (args.Count < 2
            || (args[0] is not "analyze" and not "scan"))
        {
            PrintUsage(Console.Error);
            return false;
        }

        directory = args[1];
        for (var index = 2; index < args.Count; index++)
        {
            if (args[index] != "--output" || index + 1 >= args.Count || outputPath is not null)
            {
                PrintUsage(Console.Error);
                return false;
            }

            outputPath = args[++index];
        }

        return true;
    }

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help";

    private static void PrintUsage(TextWriter writer)
        => writer.WriteLine(
            "Usage: photoculler analyze <photo-directory> [--output <result.json>]\n" +
            "       photoculler scan <photo-directory> [--output <result.json>]");

    private static void DeleteTemporaryDatabase(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
