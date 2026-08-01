namespace LgymApi.IntegrationTests;

internal static class ConcurrentFactoryCreation
{
    internal static async Task<TFactory[]> CreateAllOrDisposeSuccessfulAsync<TFactory>(
        params Func<Task<TFactory>>[] factoryCreators)
        where TFactory : IAsyncDisposable
    {
        var creationTasks = factoryCreators.Select(createFactory => createFactory()).ToArray();

        try
        {
            return await Task.WhenAll(creationTasks);
        }
        catch (Exception creationException)
        {
            List<Exception> cleanupExceptions = [];
            foreach (var successfulCreation in creationTasks.Where(task => task.IsCompletedSuccessfully))
            {
                try
                {
                    await successfulCreation.Result.DisposeAsync();
                }
                catch (Exception cleanupException)
                {
                    cleanupExceptions.Add(cleanupException);
                }
            }

            if (cleanupExceptions.Count != 0)
            {
                throw new AggregateException([creationException, .. cleanupExceptions]);
            }

            throw;
        }
    }
}
