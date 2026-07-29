using LgymApi.Resources;

namespace LgymApi.Application.WorkerRuntime;

public static class WorkerNotificationLocalization
{
    public static string GenericTrainerDisplayName => Messages.GenericTrainerDisplayName;

    public static string GenericTraineeDisplayName => Messages.GenericTraineeDisplayName;

    public static string GenericDietPlanDisplayName => Messages.GenericDietPlanDisplayName;

    public static string GenericReportDisplayName => Messages.GenericReportDisplayName;

    public static string TrainerDietPlanUpdated(string trainerName, string planName) =>
        string.Format(Messages.TrainerDietPlanUpdated, trainerName, planName);

    public static string TrainerReportRequestReceived => Messages.TrainerReportRequestReceived;

    public static string TrainerReportSubmissionReceived(string traineeName, string templateName) =>
        string.Format(Messages.TrainerReportSubmissionReceived, traineeName, templateName);

    public static string TrainerReportFeedbackReceived => Messages.TrainerReportFeedbackReceived;
}
