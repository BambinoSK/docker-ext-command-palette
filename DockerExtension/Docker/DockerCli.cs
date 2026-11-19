using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DockerExtension.Docker;

internal sealed record CliResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;

    public string Message
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Error) ? Output : Error;
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length > 0 ? lines[^1] : $"docker exited with code {ExitCode}";
        }
    }
}

/// <summary>Runs the docker CLI for features the Engine API does not cover (Compose, contexts).</summary>
internal static class DockerCli
{
    private const string ExeName = "docker.exe";

    private static string? _resolved;

    /// <summary>Full path to docker.exe, or "docker" when it cannot be found.</summary>
    public static string Executable => _resolved ?? Resolve();

    // The CLI resolves DOCKER_HOST and contexts itself; only a host set in the extension settings must be passed on.
    public static string[] GlobalArgs(DockerEndpoint endpoint) =>
        endpoint.RequiresExplicitCliHost ? ["-H", endpoint.Host] : [];

    /// <summary>Puts the CLI's folder first on PATH so its helpers (credential stores, plugins) are found too.</summary>
    public static void PrepareEnvironment(ProcessStartInfo psi)
    {
        if (Path.GetDirectoryName(Executable) is { Length: > 0 } dir)
        {
            psi.Environment["PATH"] = dir + Path.PathSeparator + psi.Environment["PATH"];
        }
    }

    public static async Task<CliResult> RunAsync(string[] args, string? workingDirectory, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        PrepareEnvironment(psi);

        foreach (var arg in GlobalArgs(DockerService.Instance.Endpoint).Concat(args))
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrEmpty(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start docker.");
        }
        catch (Win32Exception ex)
        {
            throw new DockerUnavailableException("The docker CLI was not found. Install Docker Desktop or add docker.exe to PATH.", ex);
        }

        using (process)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                return new CliResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                if (ct.IsCancellationRequested)
                {
                    throw;
                }

                throw new TimeoutException($"docker {string.Join(' ', args)} did not finish within {timeout.TotalMinutes:0} minutes.");
            }
        }
    }

    // A process keeps the PATH it started with, so Docker installed after Command Palette started
    // is only visible through the PATH stored in the registry.
    private static string Resolve()
    {
        var pathValues = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
        };

        var candidates = pathValues
            .SelectMany(value => (value ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(dir => Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), ExeName))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", ExeName));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return _resolved = candidate;
            }
        }

        // Not cached, so a later install is picked up without restarting the extension.
        return "docker";
    }
}
