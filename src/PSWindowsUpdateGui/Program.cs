using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PSWindowsUpdateGui.Cli;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui;

internal static class Program
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [STAThread]
    public static int Main(string[] args)
    {
        var isGui = args.Length == 0 || (args.Length == 1 && string.Equals(args[0], "gui", StringComparison.OrdinalIgnoreCase));
#if UI_SMOKE
        isGui = isGui || UiSmokeBootstrap.TryConfigure(args);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke-trace.log"), $"{DateTime.UtcNow:O} Program configured; isGui={isGui}{Environment.NewLine}");
#endif
        if (isGui)
        {
#if UI_SMOKE
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke-trace.log"), $"{DateTime.UtcNow:O} Starting WinUI{Environment.NewLine}");
#endif
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(initialization =>
            {
#if UI_SMOKE
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke-trace.log"), $"{DateTime.UtcNow:O} Application callback{Environment.NewLine}");
#endif
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
#if UI_SMOKE
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke-trace.log"), $"{DateTime.UtcNow:O} Application exited; code={App.ExitCode}{Environment.NewLine}");
#endif
            return App.ExitCode;
        }

        AttachParentConsole();
        try
        {
            return new CliApplication().RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            var exitCode = IsValidationFailure(exception) ? 2 : 1;
            if (RequestsJson(args))
            {
                var envelope = new Models.OperationEnvelope<object>
                {
                    Target = OptionValue(args, "computer") ?? Environment.MachineName,
                    Status = Models.OperationState.Failed,
                    CompletedUtc = DateTime.UtcNow
                };
                envelope.Errors.Add(new Models.OperationError
                {
                    Code = $"0x{exception.HResult:X8}",
                    HResult = exception.HResult,
                    Message = PortableLogService.Redact(exception.Message)
                });
                using var stream = new MemoryStream();
                new DataContractJsonSerializer(typeof(Models.OperationEnvelope<object>)).WriteObject(stream, envelope);
                Console.Out.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            }
            else Console.Error.WriteLine($"ERROR 0x{exception.HResult:X8}: {PortableLogService.Redact(exception.Message)}");
            return exitCode;
        }
    }

    private static void AttachParentConsole()
    {
        _ = AttachConsole(AttachParentProcess);
        try
        {
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(output);
            Console.SetError(error);
        }
        catch
        {
            // A detached WinExe can still run JSON jobs without an interactive console.
        }
    }

    private static bool IsValidationFailure(Exception exception) =>
        exception is FormatException ||
        exception is ArgumentException ||
        exception is PlatformNotSupportedException ||
        exception is UnauthorizedAccessException ||
        (exception is InvalidOperationException &&
         (exception.Message.IndexOf("requires --yes", StringComparison.OrdinalIgnoreCase) >= 0 ||
          exception.Message.IndexOf("noninteractively", StringComparison.OrdinalIgnoreCase) >= 0 ||
          exception.Message.IndexOf("preflight", StringComparison.OrdinalIgnoreCase) >= 0));

    private static bool RequestsJson(string[] args)
    {
        var value = OptionValue(args, "output");
        return string.Equals(value, "json", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static string? OptionValue(string[] args, string name)
    {
        var marker = "--" + name;
        var index = Array.FindLastIndex(args, value => string.Equals(value, marker, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : null;
    }
}
