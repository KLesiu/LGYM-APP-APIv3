namespace LgymApi.E2ETests.Configuration;

public sealed class E2EOptions
{
    public const string SectionName = "E2E";

    public E2EWebSourceOptions WebSource { get; set; } = new();

    public E2EApiOptions Api { get; set; } = new();

    public E2EWebOptions Web { get; set; } = new();

    public E2ERuntimeOptions Runtime { get; set; } = new();

    public E2EDatabaseOptions Database { get; set; } = new();

    public E2ETimeoutsOptions Timeouts { get; set; } = new();
}

public sealed class E2EWebSourceOptions
{
    public string RepositoryUrl { get; set; } = string.Empty;

    public string CommitSha { get; set; } = string.Empty;

    public string? SourcePath { get; set; }
}

public sealed class E2EApiOptions
{
    public string PublishedDllPath { get; set; } = string.Empty;

    public int Port { get; set; }
}

public sealed class E2EWebOptions
{
    public int Port { get; set; }
}

public sealed class E2ERuntimeOptions
{
    public string PrivateRunRoot { get; set; } = string.Empty;
}

public sealed class E2EDatabaseOptions
{
    public string Image { get; set; } = string.Empty;

    public string NamePrefix { get; set; } = string.Empty;
}

public sealed class E2ETimeoutsOptions
{
    public int ContainerStartupSeconds { get; set; }

    public int ApiStartupSeconds { get; set; }

    public int WebStartupSeconds { get; set; }

    public int ProcessShutdownSeconds { get; set; }

    public int HttpRequestSeconds { get; set; }

    public int BrowserActionMilliseconds { get; set; }

    public int ScenarioSeconds { get; set; }

    public int TestSessionSeconds { get; set; }
}
