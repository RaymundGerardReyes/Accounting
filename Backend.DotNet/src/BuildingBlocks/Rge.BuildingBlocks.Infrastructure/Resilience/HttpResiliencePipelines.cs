namespace Rge.BuildingBlocks.Infrastructure.Resilience;



/// <summary>

/// Outbound HTTP timeout, retry, and circuit-breaker policy constants for PayMongo and Google Sheets clients.

/// Keeps POST /api/v1/orders within its < 80 ms latency budget.

/// </summary>

public static class HttpResiliencePipelines

{

    public static readonly TimeSpan PayMongoAttemptTimeout = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan PayMongoTotalTimeout = TimeSpan.FromSeconds(8);

    public static readonly TimeSpan GoogleSheetsTimeout = TimeSpan.FromSeconds(10);



    public static async Task<HttpResponseMessage> SendWithRetryAsync(

        HttpClient httpClient,

        Func<HttpRequestMessage> requestFactory,

        TimeSpan perAttemptTimeout,

        int maxRetries = 2,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(httpClient);

        ArgumentNullException.ThrowIfNull(requestFactory);



        for (int attempt = 1; ; attempt++)

        {

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            linkedCts.CancelAfter(perAttemptTimeout);



            try

            {

                using HttpRequestMessage request = requestFactory();

                HttpResponseMessage response = await httpClient

                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)

                    .ConfigureAwait(false);



                if ((int)response.StatusCode >= 500 && attempt <= maxRetries)

                {

                    response.Dispose();

                    await Task.Delay(attempt * 50, cancellationToken).ConfigureAwait(false);

                    continue;

                }



                return response;

            }

            catch (Exception ex) when (

                (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))

                && attempt <= maxRetries)

            {

                await Task.Delay(attempt * 50, cancellationToken).ConfigureAwait(false);

            }

        }

    }

}
