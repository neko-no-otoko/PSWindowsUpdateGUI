using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Services;

internal sealed class PowerShellHost : IDisposable
{
    private readonly ModuleRuntime _module;
    private readonly PortableLogService _log;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private Runspace? _runspace;
    private PowerShell? _activePowerShell;
    private bool _disposed;

    public PowerShellHost(ModuleRuntime module, PortableLogService log)
    {
        _module = module;
        _log = log;
    }

    public event EventHandler<InvocationEvent>? EventReceived;

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            var state = InitialSessionState.CreateDefault();
            _runspace = RunspaceFactory.CreateRunspace(state);
            _runspace.ApartmentState = ApartmentState.STA;
            _runspace.ThreadOptions = PSThreadOptions.ReuseThread;
            _runspace.Open();

            using var importer = PowerShell.Create();
            importer.Runspace = _runspace;
            importer.AddCommand("Import-Module")
                .AddParameter("Name", _module.ModuleDllPath)
                .AddParameter("Force", true)
                .AddParameter("ErrorAction", ActionPreference.Stop);
            importer.Invoke();
            if (importer.HadErrors)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, importer.Streams.Error.Select(error => error.ToString())));
            }
        }).ConfigureAwait(false);
        _log.Write("Security", $"Loaded signed PSWindowsUpdate {_module.Manifest.PackageVersion} from the verified embedded package.");
    }

    public async Task<IReadOnlyList<CommandDefinition>> LoadCatalogAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                EnsureReady();
                using var powerShell = PowerShell.Create();
                powerShell.Runspace = _runspace;
                powerShell.AddCommand("Get-Command")
                    .AddParameter("Name", CommandRegistry.PublicCommands)
                    .AddParameter("CommandType", CommandTypes.Cmdlet);
                var definitions = new List<CommandDefinition>();
                foreach (var item in powerShell.Invoke())
                {
                    if (!(item.BaseObject is CmdletInfo command) || !CommandRegistry.IsAllowed(command.Name))
                    {
                        continue;
                    }

                    var definition = new CommandDefinition
                    {
                        Name = command.Name,
                        SupportsShouldProcess = GetSupportsShouldProcess(command)
                    };

                    foreach (var set in command.ParameterSets.OrderBy(parameterSet => parameterSet.Name))
                    {
                        var setDefinition = new ParameterSetDefinition
                        {
                            Name = set.Name,
                            IsDefault = set.IsDefault
                        };
                        foreach (var parameter in set.Parameters
                                     .Where(parameter => !CommandRegistry.CommonParameters.Contains(parameter.Name))
                                     .OrderByDescending(parameter => parameter.IsMandatory)
                                     .ThenBy(parameter => parameter.Name))
                        {
                            var parameterDefinition = new ParameterDefinition
                            {
                                Name = parameter.Name,
                                ParameterType = parameter.ParameterType,
                                IsMandatory = parameter.IsMandatory
                            };
                            foreach (var attribute in parameter.Attributes)
                            {
                                if (attribute is ValidateSetAttribute validateSet)
                                {
                                    foreach (var value in validateSet.ValidValues)
                                    {
                                        parameterDefinition.ValidValues.Add(value);
                                    }
                                }
                                else if (attribute is ValidateRangeAttribute validateRange)
                                {
                                    parameterDefinition.Minimum = validateRange.MinRange;
                                    parameterDefinition.Maximum = validateRange.MaxRange;
                                }
                            }

                            setDefinition.Parameters.Add(parameterDefinition);
                        }

                        definition.ParameterSets.Add(setDefinition);
                    }

                    definitions.Add(definition);
                }

                var ordered = CommandRegistry.PublicCommands
                    .Select(name => definitions.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase)))
                    .Where(command => command != null)
                    .Cast<CommandDefinition>()
                    .ToList();
                return (IReadOnlyList<CommandDefinition>)ordered;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InvocationResult> InvokeAsync(
        string command,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (!CommandRegistry.IsAllowed(command))
        {
            throw new InvalidOperationException($"Command is not allowlisted: {command}");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => InvokeCore(command, parameters, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Stop()
    {
        try
        {
            _activePowerShell?.Stop();
        }
        catch
        {
            // Stop is best effort. Windows Update operations might continue outside the observing pipeline.
        }
    }

    public string RenderPreview(string command, IReadOnlyDictionary<string, object?> parameters)
    {
        var parts = new List<string> { command };
        foreach (var pair in parameters.OrderBy(pair => pair.Key))
        {
            if (pair.Value is PSCredential)
            {
                parts.Add($"-{pair.Key} <credential>");
            }
            else if (pair.Value is SwitchParameter switchValue)
            {
                parts.Add($"-{pair.Key}:${switchValue.IsPresent.ToString().ToLowerInvariant()}");
            }
            else if (pair.Value is bool boolValue)
            {
                parts.Add($"-{pair.Key}:${boolValue.ToString().ToLowerInvariant()}");
            }
            else if (pair.Value is Array array)
            {
                parts.Add($"-{pair.Key} @({string.Join(", ", array.Cast<object>().Select(Quote))})");
            }
            else
            {
                parts.Add($"-{pair.Key} {Quote(pair.Value)}");
            }
        }

        return PortableLogService.Redact(string.Join(" ", parts));
    }

    private InvocationResult InvokeCore(string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        EnsureReady();
        var result = new InvocationResult();
        using var powerShell = PowerShell.Create();
        _activePowerShell = powerShell;
        powerShell.Runspace = _runspace;
        powerShell.AddCommand(command);
        foreach (var pair in parameters)
        {
            powerShell.AddParameter(pair.Key, pair.Value);
        }

        if (!parameters.ContainsKey("ErrorAction"))
        {
            powerShell.AddParameter("ErrorAction", ActionPreference.Stop);
        }

        HookStreams(powerShell, result);
        var output = new PSDataCollection<PSObject>();
        output.DataAdded += (_, args) =>
        {
            var value = output[args.Index];
            lock (result.Output)
            {
                result.Output.Add(value);
            }
            Raise(InvocationEventKind.Output, value.ToString());
        };

        using var registration = cancellationToken.Register(() =>
        {
            result.WasCancelled = true;
            try { powerShell.Stop(); } catch { }
        });

        try
        {
            var input = new PSDataCollection<PSObject>();
            input.Complete();
            var invocation = powerShell.BeginInvoke<PSObject, PSObject>(input, output);
            powerShell.EndInvoke(invocation);
        }
        catch (PipelineStoppedException) when (cancellationToken.IsCancellationRequested)
        {
            result.WasCancelled = true;
        }
        catch (Exception exception)
        {
            result.Errors.Add(exception.Message);
            Raise(InvocationEventKind.Error, exception.Message);
        }
        finally
        {
            _activePowerShell = null;
        }

        return result;
    }

    private void HookStreams(PowerShell powerShell, InvocationResult result)
    {
        powerShell.Streams.Error.DataAdded += (_, args) =>
        {
            var message = powerShell.Streams.Error[args.Index].ToString();
            result.Errors.Add(message);
            Raise(InvocationEventKind.Error, message);
        };
        powerShell.Streams.Warning.DataAdded += (_, args) =>
            Raise(InvocationEventKind.Warning, powerShell.Streams.Warning[args.Index].Message);
        powerShell.Streams.Verbose.DataAdded += (_, args) =>
            Raise(InvocationEventKind.Verbose, powerShell.Streams.Verbose[args.Index].Message);
        powerShell.Streams.Information.DataAdded += (_, args) =>
            Raise(InvocationEventKind.Information, powerShell.Streams.Information[args.Index].MessageData?.ToString() ?? string.Empty);
        powerShell.Streams.Progress.DataAdded += (_, args) =>
        {
            var progress = powerShell.Streams.Progress[args.Index];
            Raise(InvocationEventKind.Progress, $"{progress.Activity}: {progress.StatusDescription}",
                progress.PercentComplete >= 0 ? progress.PercentComplete : (int?)null);
        };
    }

    private void Raise(InvocationEventKind kind, string message, int? percent = null)
    {
        _log.Write(kind.ToString(), message);
        EventReceived?.Invoke(this, new InvocationEvent(kind, message, percent));
    }

    private static bool GetSupportsShouldProcess(CmdletInfo command)
    {
        var attribute = command.ImplementingType
            .GetCustomAttributes(typeof(CmdletAttribute), true)
            .OfType<CmdletAttribute>()
            .FirstOrDefault();
        return attribute?.SupportsShouldProcess ?? false;
    }

    private static string Quote(object? value)
    {
        if (value == null)
        {
            return "$null";
        }

        if (value is DateTime dateTime)
        {
            return $"[datetime]'{dateTime:O}'";
        }

        var text = value.ToString() ?? string.Empty;
        return "'" + text.Replace("'", "''") + "'";
    }

    private void EnsureReady()
    {
        if (_disposed || _runspace == null || _runspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
            throw new InvalidOperationException("The Windows PowerShell host is not ready.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activePowerShell?.Dispose();
        _runspace?.Close();
        _runspace?.Dispose();
        _gate.Dispose();
    }
}
