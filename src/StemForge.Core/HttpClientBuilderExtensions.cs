using Microsoft.Extensions.DependencyInjection;

namespace StemForge.Core;

public static class HttpClientBuilderExtensions
{
    public static IHttpClientBuilder WithUserAgent(this IHttpClientBuilder builder) =>
        builder.ConfigureHttpClient(
            (sp, client) =>
            {
                var appInfo = sp.GetRequiredService<IAppInfo>();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"{appInfo.ProductName}/{appInfo.ShortVersion}"
                );
            }
        );

    public static IHttpClientBuilder WithHeaders(
        this IHttpClientBuilder builder,
        Dictionary<string, string> headers
    ) =>
        builder.ConfigureHttpClient(client =>
        {
            foreach (var header in headers)
                client.DefaultRequestHeaders.Add(header.Key, header.Value);
        });

    public static IHttpClientBuilder WithTimeout(
        this IHttpClientBuilder builder,
        TimeSpan timeout
    ) => builder.ConfigureHttpClient(client => client.Timeout = timeout);
}
