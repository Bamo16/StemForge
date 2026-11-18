using Microsoft.Extensions.DependencyInjection;

namespace StemForge.Tests;

public sealed class HttpClientBuilderExtensionsTests
{
    private static IHttpClientFactory BuildFactory(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppInfo>(new AppInfo("StemForge", new Version(1, 2, 3, 4)));
        configure(services);

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public void WithUserAgent_SendsTheProductNameAndThreePartVersion()
    {
        var factory = BuildFactory(services => services.AddHttpClient("test").WithUserAgent());

        var client = factory.CreateClient("test");

        Assert.Equal("StemForge/1.2.3", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void WithHeaders_AddsEveryHeaderToTheClient()
    {
        var factory = BuildFactory(services =>
            services
                .AddHttpClient("test")
                .WithHeaders(new() { ["Accept"] = "application/json", ["X-Probe"] = "yes" })
        );

        var client = factory.CreateClient("test");

        Assert.Equal(
            "application/json",
            Assert.Single(client.DefaultRequestHeaders.GetValues("Accept"))
        );
        Assert.Equal("yes", Assert.Single(client.DefaultRequestHeaders.GetValues("X-Probe")));
    }

    [Fact]
    public void WithTimeout_SetsTheClientTimeout()
    {
        var factory = BuildFactory(services =>
            services.AddHttpClient("test").WithTimeout(TimeSpan.FromMinutes(15))
        );

        Assert.Equal(TimeSpan.FromMinutes(15), factory.CreateClient("test").Timeout);
    }

    [Fact]
    public void DefaultsPlusANamedOverride_TheNamedClientKeepsTheUserAgentButWinsTheTimeout()
    {
        // The shape CoreServiceExtensions registers: defaults for every client, "bundled" longer.
        var factory = BuildFactory(services =>
        {
            services.ConfigureHttpClientDefaults(builder =>
                builder.WithUserAgent().WithTimeout(TimeSpan.FromSeconds(10))
            );
            services.AddHttpClient("bundled").WithTimeout(TimeSpan.FromMinutes(15));
            services.AddHttpClient("plain");
        });

        var bundled = factory.CreateClient("bundled");
        var plain = factory.CreateClient("plain");

        Assert.Equal(TimeSpan.FromMinutes(15), bundled.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), plain.Timeout);
        Assert.Equal("StemForge/1.2.3", bundled.DefaultRequestHeaders.UserAgent.ToString());
    }
}
