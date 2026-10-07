#:package AWSSDK.CognitoIdentityProvider@4.0.105.3

// AWS Cognito user pools against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// On AWS, two pool settings stand between a user and a password sign-in: the app client has to list
// ALLOW_USER_PASSWORD_AUTH in its ExplicitAuthFlows, and the password has to satisfy the pool's
// policy. This lab gives a user the password "abc", signs in through a client that allows the flow,
// then tries again through one that doesn't. It prints what Floci said and which way each went.

using System.Text;
using System.Text.Json;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Runtime;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = (Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566").TrimEnd('/');

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0 makes
// a failure show at once instead of after ~8 s of retries; against real AWS, keep retries.
AmazonCognitoIdentityProviderConfig config = new AmazonCognitoIdentityProviderConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 };
using AmazonCognitoIdentityProviderClient cognito = new AmazonCognitoIdentityProviderClient(new BasicAWSCredentials("test", "test"), config);

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

const string UserName = "alice";
const string WeakPassword = "abc";

string poolName = $"lab-pool-{Guid.NewGuid().ToString("N")[..8]}";
string poolId = (await cognito.CreateUserPoolAsync(new CreateUserPoolRequest { PoolName = poolName })).UserPool.Id;
Console.WriteLine($"CreateUserPool {poolName} -> {poolId} (default password policy)");

try
{
    string allowed = (await cognito.CreateUserPoolClientAsync(new CreateUserPoolClientRequest { UserPoolId = poolId, ClientName = "password-auth", ExplicitAuthFlows = ["ALLOW_USER_PASSWORD_AUTH"] })).UserPoolClient.ClientId;
    string bare = (await cognito.CreateUserPoolClientAsync(new CreateUserPoolClientRequest { UserPoolId = poolId, ClientName = "no-auth-flows" })).UserPoolClient.ClientId;
    Console.WriteLine($"CreateUserPoolClient x2      -> {allowed} (ALLOW_USER_PASSWORD_AUTH), {bare} (no flows)");

    await cognito.AdminCreateUserAsync(new AdminCreateUserRequest { UserPoolId = poolId, Username = UserName, MessageAction = MessageActionType.SUPPRESS });

    // 1. The password policy. A default pool on AWS wants 8+ characters, upper, lower, digit and symbol.
    bool weakPasswordAccepted;
    try
    {
        await cognito.AdminSetUserPasswordAsync(new AdminSetUserPasswordRequest { UserPoolId = poolId, Username = UserName, Password = WeakPassword, Permanent = true });
        weakPasswordAccepted = true;
        Console.WriteLine($"AdminSetUserPassword \"{WeakPassword}\"   -> accepted");
    }
    catch (InvalidPasswordException ex)
    {
        weakPasswordAccepted = false;
        Console.WriteLine($"AdminSetUserPassword \"{WeakPassword}\"   -> refused: InvalidPasswordException: {ex.Message}");
    }

    // Sign in with whichever password the user actually has, so the flow check below still runs.
    string password = weakPasswordAccepted ? WeakPassword : "Lab-Passw0rd!";
    if (!weakPasswordAccepted)
    {
        await cognito.AdminSetUserPasswordAsync(new AdminSetUserPasswordRequest { UserPoolId = poolId, Username = UserName, Password = password, Permanent = true });
    }

    InitiateAuthResponse auth = await cognito.InitiateAuthAsync(new InitiateAuthRequest
    {
        ClientId = allowed,
        AuthFlow = AuthFlowType.USER_PASSWORD_AUTH,
        AuthParameters = new Dictionary<string, string> { ["USERNAME"] = UserName, ["PASSWORD"] = password },
    });

    // The token is live on AWS, so only its issuer claim is printed.
    string payload = auth.AuthenticationResult.AccessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
    payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
    string issuer = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement.GetProperty("iss").GetString() ?? "(none)";
    Console.WriteLine($"InitiateAuth, flow allowed   -> signed in with \"{password}\", access token iss: {issuer}");

    // 2. The client's auth flows. Floci 2.1.0 ignored them; this tells you which way it goes now.
    try
    {
        await cognito.InitiateAuthAsync(new InitiateAuthRequest
        {
            ClientId = bare,
            AuthFlow = AuthFlowType.USER_PASSWORD_AUTH,
            AuthParameters = new Dictionary<string, string> { ["USERNAME"] = UserName, ["PASSWORD"] = password },
        });
        Console.WriteLine("InitiateAuth, no flows        -> signed in (AWS refuses this: flow not enabled for this client)");
    }
    catch (InvalidParameterException ex)
    {
        Console.WriteLine($"InitiateAuth, no flows        -> refused: {ex.Message}");
    }

    Console.WriteLine();
    Console.WriteLine(weakPasswordAccepted
        ? $"Floci accepted \"{WeakPassword}\" as a password and signed alice in with it. On AWS the default policy\n  refuses it with InvalidPasswordException. A fixture with a short hard-coded password passes here\n  and fails against AWS: generate one that satisfies the real policy."
        : "Floci enforces the pool's password policy, as AWS does.");
}
finally
{
    // Deleting the pool takes the clients and the user with it, so a second run starts clean.
    await cognito.DeleteUserPoolAsync(new DeleteUserPoolRequest { UserPoolId = poolId });
    Console.WriteLine();
    Console.WriteLine($"DeleteUserPool {poolId} -> done");
}
