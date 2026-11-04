using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.SharedKernel.Results;
using Xunit;

namespace Rge.Contract.Parity.Tests;

public sealed class ProblemDetailsFactoryTests
{
    [Fact]
    public void Create_WhenRequestOnLocalhost_UsesLocalhostSchemeAndPort()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost", 5080);
        httpContext.Request.Path = "/api/auth/login";

        var error = Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid email or password.");

        // Act
        var problem = ProblemDetailsFactory.Create(httpContext, error);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        Assert.Equal("Unauthorized", problem.Title);
        Assert.Equal("http://localhost:5080/errors/AUTH_INVALID_CREDENTIALS", problem.Type);
        Assert.Equal("Invalid email or password.", problem.Detail);
        Assert.Equal("/api/auth/login", problem.Instance);
        Assert.Equal("AUTH_INVALID_CREDENTIALS", problem.Extensions["error_code"]);
    }

    [Fact]
    public void Create_WhenRequestOn127_Uses127SchemeAndPort()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("127.0.0.1", 5080);
        httpContext.Request.Path = "/api/v1/orders";

        var error = Error.Validation("ERR_INVALID_INPUT", "Order number cannot be empty.");

        // Act
        var problem = ProblemDetailsFactory.Create(httpContext, error);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("http://127.0.0.1:5080/errors/ERR_INVALID_INPUT", problem.Type);
    }

    [Fact]
    public void Create_WhenRequestHostEmpty_FallsBackToRgeAccountingLocal()
    {
        // Arrange - Bare context with empty Host
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/payments";

        var error = Error.NotFound("ERR_PAYMENT_NOT_FOUND", "Payment was not found.");

        // Act
        var problem = ProblemDetailsFactory.Create(httpContext, error);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("https://rge-accounting.local/errors/ERR_PAYMENT_NOT_FOUND", problem.Type);
    }

    [Fact]
    public void Create_WhenForwardedHeadersPresentOnProduction_UsesForwardedDomain()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("internal-cluster-proxy", 80);
        httpContext.Request.Headers["X-Forwarded-Proto"] = "https";
        httpContext.Request.Headers["X-Forwarded-Host"] = "rge-accounting-3aa707d5f496.herokuapp.com";
        httpContext.Request.Path = "/api/auth/login";

        var error = Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid credentials");

        // Act
        var problem = ProblemDetailsFactory.Create(httpContext, error);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("https://rge-accounting-3aa707d5f496.herokuapp.com/errors/AUTH_INVALID_CREDENTIALS", problem.Type);
    }

    [Fact]
    public void Create_WhenProductionDomainConfigured_UsesConfiguredDomain()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NEXT_PUBLIC_APP_DOMAIN"] = "https://accounting.my-company.com"
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("accounting.my-company.com");
        httpContext.Request.Path = "/api/v1/orders";

        var error = Error.Conflict("ORDER_ALREADY_PAID", "Order is already paid.");

        // Act
        var problem = ProblemDetailsFactory.Create(httpContext, error);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("https://accounting.my-company.com/errors/ORDER_ALREADY_PAID", problem.Type);
    }

    [Fact]
    public void Create_WhenOnLocalhostWithHttps_EmitsHttpsLocalhostType()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost", 7080);
        httpContext.Request.Path = "/api/auth/session";

        var error = Error.Unauthorized("SESSION_EXPIRED", "Session expired.");

        // Act
        var problem = ProblemDetailsFactory.Create(httpContext, error);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("https://localhost:7080/errors/SESSION_EXPIRED", problem.Type);
    }
}

