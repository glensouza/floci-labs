#:package AWSSDK.CognitoIdentityProvider@4.0.105.3

// AWS Cognito user pools against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs
//
// On AWS, two pool settings stand between a user and a password sign-in: the app client has to list
// ALLOW_USER_PASSWORD_AUTH in its ExplicitAuthFlows, and the password has to satisfy the pool's
// policy. This lab breaks both on purpose (a client with no auth flows, a password of "abc") and
// then signs in. It prints what Floci said and which way it went, so it tells you if Floci ever
// starts checking either.

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

string poolName = $"lab-lax-pool-{Guid.NewGuid().ToString("N")[..8]}";
string poolId = (await cognito.CreateUserPoolAsync(new CreateUserPoolRequest { PoolName = poolName })).UserPool.Id;
Console.WriteLine($"CreateUserPool {poolName} -> {poolId} (default password policy)");

try
{
    // No ExplicitAuthFlows at all. On AWS this client cannot use USER_PASSWORD_AUTH.
    CreateUserPoolClientResponse created = await cognito.CreateUserPoolClientAsync(new CreateUserPoolClientRequest { UserPoolId = poolId, ClientName = "no-auth-flows" });
    string clientId = created.UserPoolClient.ClientId;
    Console.WriteLine($"CreateUserPoolClient        -> {clientId}, ExplicitAuthFlows: [{string.Join(", ", created.UserPoolClient.ExplicitAuthFlows ?? [])}]");

    await cognito.AdminCreateUserAsync(new AdminCreateUserRequest { UserPoolId = poolId, Username = UserName, MessageAction = MessageActionType.SUPPRESS });

    bool weakPasswordAccepted;
    try
    {
        await cognito.AdminSetUserPasswordAsync(new AdminSetUserPasswordRequest { UserPoolId = poolId, Username = UserName, Password = WeakPassword, Permanent = true });
        weakPasswordAccepted = true;
        Console.WriteLine($"AdminSetUserPassword \"{WeakPassword}\"  -> accepted");
    }
    catch (InvalidPasswordException ex)
    {
        weakPasswordAccepted = false;
        Console.WriteLine($"AdminSetUserPassword \"{WeakPassword}\"  -> refused: InvalidPasswordException: {ex.Message}");
    }

    // Sign in with whichever password the user actually has. If "abc" was refused, set one that
    // satisfies the default policy, so the auth-flow check is still tested on its own.
    string password = weakPasswordAccepted ? WeakPassword : "Lab-Passw0rd!";
    if (!weakPasswordAccepted)
    {
        await cognito.AdminSetUserPasswordAsync(new AdminSetUserPasswordRequest { UserPoolId = poolId, Username = UserName, Password = password, Permanent = true });
    }

    try
    {
        InitiateAuthResponse auth = await cognito.InitiateAuthAsync(new InitiateAuthRequest
        {
            ClientId = clientId,
            AuthFlow = AuthFlowType.USER_PASSWORD_AUTH,
            AuthParameters = new Dictionary<string, string> { ["USERNAME"] = UserName, ["PASSWORD"] = password },
        });

        // The token is live on AWS, so only its issuer claim is printed.
        string payload = auth.AuthenticationResult.AccessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        string issuer = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement.GetProperty("iss").GetString() ?? "(none)";

        Console.WriteLine($"InitiateAuth USER_PASSWORD_AUTH -> signed in, access token iss: {issuer}");
        Console.WriteLine();
        Console.WriteLine("Floci signed alice in through a client that never allowed USER_PASSWORD_AUTH.\n  On AWS this answers InvalidParameterException: USER_PASSWORD_AUTH flow not enabled for this client.\n  Put ALLOW_USER_PASSWORD_AUTH (or the flow you use) in the client's ExplicitAuthFlows.");
    }
    catch (InvalidParameterException ex)
    {
        Console.WriteLine($"InitiateAuth USER_PASSWORD_AUTH -> refused: InvalidParameterException: {ex.Message}");
        Console.WriteLine();
        Console.WriteLine("Floci now checks the client's ExplicitAuthFlows, as AWS does.");
    }

    Console.WriteLine(weakPasswordAccepted
        ? $"Floci accepted \"{WeakPassword}\" as a password. On AWS the default policy refuses it with InvalidPasswordException\n  (8+ characters, upper, lower, digit and symbol). A green run here proves your calls, not your pool settings."
        : "Floci now enforces the pool's password policy, as AWS does.");
}
finally
{
    // Deleting the pool takes the client and the user with it, so a second run starts clean.
    await cognito.DeleteUserPoolAsync(new DeleteUserPoolRequest { UserPoolId = poolId });
    Console.WriteLine();
    Console.WriteLine($"DeleteUserPool {poolId} -> done");
}
