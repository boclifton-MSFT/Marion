using System.Data.Common;
using Azure.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Marion.ApiService.Infrastructure.Persistence;

internal sealed class SqlEntraConnectionInterceptor : DbConnectionInterceptor
{
    private readonly Func<
        SqlAuthenticationParameters,
        CancellationToken,
        Task<SqlAuthenticationToken>> accessTokenCallback;

    public SqlEntraConnectionInterceptor(TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        Credential = credential;
        accessTokenCallback = GetAccessTokenAsync;
    }

    internal TokenCredential Credential { get; }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ConfigureConnection(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ConfigureConnection(connection);
        return ValueTask.FromResult(result);
    }

    internal void ConfigureConnection(DbConnection connection)
    {
        if (connection is not SqlConnection sqlConnection)
        {
            return;
        }

        sqlConnection.AccessTokenCallback = accessTokenCallback;
    }

    private async Task<SqlAuthenticationToken> GetAccessTokenAsync(
        SqlAuthenticationParameters authenticationParameters,
        CancellationToken cancellationToken)
    {
        var resource = authenticationParameters.Resource;
        var scope = resource.EndsWith(
                "/.default",
                StringComparison.OrdinalIgnoreCase)
            ? resource
            : $"{resource}/.default";
        var token = await Credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            cancellationToken);

        return new SqlAuthenticationToken(token.Token, token.ExpiresOn);
    }
}
