namespace LgymApi.Platform.Persistence;

internal interface ICommandEnvelopeDuplicateFailureClassifier
{
    bool IsDuplicateCorrelationFailure(Exception commitFailure);
}
