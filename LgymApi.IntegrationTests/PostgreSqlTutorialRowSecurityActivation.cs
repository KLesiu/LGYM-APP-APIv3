using System.ComponentModel;
using System.Diagnostics;
using Npgsql;

namespace LgymApi.IntegrationTests;

internal static class PostgreSqlTutorialRowSecurityActivation
{
    private const string ScriptRelativePath = "deploy/postgres/activate-tutorial-row-security.sql";

    public static async Task RunAsync(
        string maintenanceConnectionString,
        string databaseName,
        string maintenanceRole,
        string runtimeRole)
    {
        var connection = new NpgsqlConnectionStringBuilder(maintenanceConnectionString);
        var scriptPath = Path.Combine(FindRepositoryRoot(), ScriptRelativePath);

        await RunScriptAsync(CreateProcessStartInfo(connection, databaseName, maintenanceRole, runtimeRole), scriptPath);
        await RunScriptAsync(CreateProcessStartInfo(connection, databaseName, maintenanceRole, runtimeRole), scriptPath);
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        NpgsqlConnectionStringBuilder connection,
        string databaseName,
        string maintenanceRole,
        string runtimeRole)
    {
        var runnerContainer = FindRunnerContainer();
        var host = connection.Host ?? throw new InvalidOperationException("The maintenance connection must include a host.");
        var username = connection.Username ?? throw new InvalidOperationException("The maintenance connection must include a username.");
        var database = connection.Database ?? throw new InvalidOperationException("The maintenance connection must include a database.");
        var startInfo = new ProcessStartInfo
        {
            FileName = runnerContainer is null ? "psql" : "docker",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["PGPASSWORD"] = connection.Password;

        if (runnerContainer is not null)
        {
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add("PGPASSWORD");
            startInfo.ArgumentList.Add(runnerContainer);
            startInfo.ArgumentList.Add("psql");
        }

        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("ON_ERROR_STOP=1");
        AddVariable(startInfo, "database_name", databaseName);
        AddVariable(startInfo, "target_environment", "Staging");
        AddVariable(startInfo, "maintenance_role", maintenanceRole);
        AddVariable(startInfo, "runtime_role", runtimeRole);
        startInfo.ArgumentList.Add("-h");
        startInfo.ArgumentList.Add(runnerContainer is null ? host : "127.0.0.1");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add((runnerContainer is null ? connection.Port : 5432).ToString());
        startInfo.ArgumentList.Add("-U");
        startInfo.ArgumentList.Add(username);
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(database);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("-");
        return startInfo;
    }

    private static async Task RunScriptAsync(ProcessStartInfo startInfo, string scriptPath)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Could not start psql or the PostgreSQL runner container command.", exception);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await using (var script = File.OpenRead(scriptPath))
        {
            await script.CopyToAsync(process.StandardInput.BaseStream);
        }

        process.StandardInput.Close();
        await Task.WhenAll(process.WaitForExitAsync(), standardOutput, standardError);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Tutorial RLS activation command failed with exit code {process.ExitCode}.");
        }
    }

    private static string? FindRunnerContainer()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("ps");
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add("name=lgym-postgres-tests-");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.Names}}");
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SingleOrDefault()
            : null;
    }

    private static void AddVariable(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add($"{name}={value}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root for the tutorial RLS activation script.");
    }
}
