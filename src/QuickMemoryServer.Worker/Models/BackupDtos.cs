using System.ComponentModel.DataAnnotations;
using NCrontab;
using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Models;

public sealed record BackupSettingsRequest(
    [Required] string DifferentialCron,
    [Required] string FullCron,
    [Range(0, int.MaxValue)] int RetentionDays,
    [Range(0, int.MaxValue)] int FullRetentionDays,
    string? TargetPath);

public sealed record BackupProbeRequest(string? TargetPath);

public sealed record BackupRunRequest(string Endpoint, string? Mode);

public sealed record BackupSettingsValidation(bool IsValid, IReadOnlyList<string> Errors);

public static class BackupSettingsValidator
{
    public static BackupSettingsValidation Validate(BackupSettingsRequest request)
    {
        var errors = new List<string>();
        if (!IsValidCron(request.DifferentialCron))
        {
            errors.Add("invalid-differential-cron");
        }

        if (!IsValidCron(request.FullCron))
        {
            errors.Add("invalid-full-cron");
        }

        if (request.RetentionDays < 0)
        {
            errors.Add("retentionDays-must-be-nonnegative");
        }

        if (request.FullRetentionDays < 0)
        {
            errors.Add("fullRetentionDays-must-be-nonnegative");
        }

        return new BackupSettingsValidation(errors.Count == 0, errors);
    }

    private static bool IsValidCron(string cron)
    {
        try
        {
            CrontabSchedule.Parse(cron, new CrontabSchedule.ParseOptions { IncludingSeconds = false });
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public static class BackupServicePreview
{
    public static DateTime? NextRun(string cron, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return null;
        }

        try
        {
            var schedule = CrontabSchedule.Parse(cron, new CrontabSchedule.ParseOptions { IncludingSeconds = false });
            return schedule.GetNextOccurrence(now);
        }
        catch
        {
            return null;
        }
    }

    public static void ProbeWritable(string targetPath)
    {
        var root = Path.Combine(targetPath, "Backups");
        Directory.CreateDirectory(root);
        var probePath = Path.Combine(root, ".probe");
        using var stream = new FileStream(probePath, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
    }
}
