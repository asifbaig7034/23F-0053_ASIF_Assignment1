namespace MarksApi.SmokeTests;

public static class SmokeTestHost
{
    public static async Task<int> RunAsync(WebApplication app)
    {
        var exitCode = 1;

        try
        {
            await app.StartAsync();

            var address = app.Urls.FirstOrDefault(url =>
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            if (address is null)
                throw new InvalidOperationException(
                    "Start with the https launch profile.");

            await using var scope = app.Services.CreateAsyncScope();

            var runner = scope.ServiceProvider
                .GetRequiredService<SmokeTestRunner>();

            exitCode = await runner.RunAsync(
                new Uri(address),
                app.Lifetime.ApplicationStopping);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"FAIL smoke startup: {exception.GetType().Name}");
        }
        finally
        {
            using var shutdownTimeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                await app.StopAsync(shutdownTimeout.Token);
                await app.DisposeAsync();
            }
            catch (Exception exception)
            {
                exitCode = 1;
                Console.Error.WriteLine(
                    $"FAIL smoke shutdown: {exception.GetType().Name}");
            }
        }

        return exitCode;
    }
}
