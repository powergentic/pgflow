using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Powergentic.Flow.Actions.GitHubCopilot;

public sealed class CopilotInstallationProbe : ICopilotInstallationProbe
{
    private static readonly Regex VersionPattern = new(@"\b\d+\.\d+(?:\.\d+)?(?:[-+][A-Za-z0-9.-]+)?\b", RegexOptions.Compiled);

    public bool IsInstalled()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "copilot",
                    Arguments = "--version",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            process.StandardInput.Close();

            if (!process.WaitForExit(50))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return false;
            }

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            var output = string.Join(Environment.NewLine, [standardOutput, standardError])
                .Trim();

            return !string.IsNullOrWhiteSpace(output) && VersionPattern.IsMatch(output);
        }
        catch
        {
            return false;
        }
    }
}
