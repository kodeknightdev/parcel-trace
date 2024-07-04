using System.Diagnostics;
using Xunit;

namespace ParcelTrace.TestSupport;

public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Available = new(Probe);

    public static bool IsDockerAvailable => Available.Value;

    public DockerFactAttribute()
    {
        if (!Available.Value)
        {
            Skip = "Docker is not available; Testcontainers tests skipped.";
        }
    }

    private static bool Probe()
    {
        if (Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS") == "true")
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null || !process.WaitForExit(10_000))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
