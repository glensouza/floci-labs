#:package AWSSDK.StepFunctions@4.0.100.13

// Step Functions against Floci, with the official AWS SDK for .NET and nothing else.
// Run it with: dotnet run lab.cs   (takes a few seconds: one state machine waits 3 s on purpose)
//
// The only Floci-specific lines are the endpoint, the dummy credentials, MaxErrorRetry and the
// made-up role ARN. Against real AWS the role has to exist and be assumable by states.amazonaws.com.

using System.Diagnostics;
using Amazon.Runtime;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;

// 127.0.0.1, not localhost: localhost also resolves to ::1, a published Docker port binds IPv4
// only, and on Windows every new connection waits out a dead IPv6 attempt first (~2 s).
string endpoint = Environment.GetEnvironmentVariable("FLOCI_ENDPOINT") ?? "http://127.0.0.1:4566";

// Floci parses SigV4 but does not verify it, so any well-formed pair works. MaxErrorRetry = 0
// makes a stopped emulator fail fast instead of retrying for ~8 s; against real AWS, keep retries.
BasicAWSCredentials credentials = new BasicAWSCredentials("test", "test");
using AmazonStepFunctionsClient sfn = new AmazonStepFunctionsClient(credentials, new AmazonStepFunctionsConfig { ServiceURL = endpoint, AuthenticationRegion = "us-east-1", MaxErrorRetry = 0 });

// Fresh names per run, so a crashed run never breaks the next one.
string suffix = Guid.NewGuid().ToString("N")[..12];
const string roleArn = "arn:aws:iam::000000000000:role/states-role-that-does-not-exist";
const string passDefinition = """{"StartAt":"Done","States":{"Done":{"Type":"Pass","Result":"ok","End":true}}}""";
const string waitDefinition = """{"StartAt":"Pause","States":{"Pause":{"Type":"Wait","Seconds":3,"Next":"Done"},"Done":{"Type":"Pass","Result":"ok","End":true}}}""";
List<string> stateMachines = [];

Console.WriteLine($"Floci endpoint: {endpoint}");
Console.WriteLine();

try
{
    // 1. The one that matters: what does the first DescribeExecution say?
    string passArn = await CreateAsync($"pass-{suffix}", passDefinition);
    StartExecutionResponse started = await sfn.StartExecutionAsync(new StartExecutionRequest { StateMachineArn = passArn, Name = $"run-{suffix}" });
    DescribeExecutionResponse immediate = await sfn.DescribeExecutionAsync(new DescribeExecutionRequest { ExecutionArn = started.ExecutionArn });
    Console.WriteLine("StartExecution (one Pass state), then DescribeExecution straight away");
    Console.WriteLine($"  Status: {immediate.Status}, Output: {immediate.Output}");
    Console.WriteLine(immediate.Status == ExecutionStatus.RUNNING
        ? "  Still RUNNING, like real Step Functions: a poll loop is exercised even here."
        : "  Already finished. Real Step Functions returns from StartExecution while the execution is still\n  RUNNING, so code that reads one status here and stops will pass locally and fail on AWS.");
    Console.WriteLine();

    // 2. Does a Wait state make it asynchronous?
    string waitArn = await CreateAsync($"wait-{suffix}", waitDefinition);
    StartExecutionResponse waiting = await sfn.StartExecutionAsync(new StartExecutionRequest { StateMachineArn = waitArn, Name = $"run-{suffix}" });
    Stopwatch stopwatch = Stopwatch.StartNew();
    DescribeExecutionResponse first = await sfn.DescribeExecutionAsync(new DescribeExecutionRequest { ExecutionArn = waiting.ExecutionArn });
    Console.WriteLine("StartExecution (a 3 s Wait state, then Pass), then DescribeExecution straight away");
    Console.WriteLine($"  First read: {first.Status}");

    DescribeExecutionResponse last = first;
    while (last.Status == ExecutionStatus.RUNNING && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
    {
        await Task.Delay(500);
        last = await sfn.DescribeExecutionAsync(new DescribeExecutionRequest { ExecutionArn = waiting.ExecutionArn });
    }

    Console.WriteLine($"  Settled as {last.Status} after {stopwatch.Elapsed.TotalSeconds:F1} s");
    Console.WriteLine(first.Status == ExecutionStatus.RUNNING
        ? "  So Floci is asynchronous when the definition takes time; only work that finishes instantly beats the read."
        : "  A Wait state did not slow it down: this build runs the whole definition before StartExecution returns.");
    Console.WriteLine();

    // 3. What the create call checks, and what it does not.
    try
    {
        await sfn.CreateStateMachineAsync(new CreateStateMachineRequest { Name = $"bad-role-{suffix}", Definition = passDefinition, RoleArn = "not-an-arn" });
        Console.WriteLine("CreateStateMachine with RoleArn \"not-an-arn\" -> accepted");
    }
    catch (AmazonStepFunctionsException ex)
    {
        Console.WriteLine($"CreateStateMachine with RoleArn \"not-an-arn\" -> {ex.ErrorCode}");
    }

    try
    {
        await sfn.CreateStateMachineAsync(new CreateStateMachineRequest { Name = $"bad-json-{suffix}", Definition = "{ not json", RoleArn = roleArn });
        Console.WriteLine("CreateStateMachine with a malformed definition -> accepted");
    }
    catch (AmazonStepFunctionsException ex)
    {
        Console.WriteLine($"CreateStateMachine with a malformed definition -> {ex.ErrorCode}: {ex.Message}");
    }

    Console.WriteLine("CreateStateMachine with a role that does not exist -> accepted (that is the RoleArn every call above used)");
    Console.WriteLine("  Real Step Functions checks the role at create time, so a definition that runs here can still\n  fail on deploy, purely on IAM.");
}
finally
{
    // Runs even when a step above throws.
    foreach (string arn in stateMachines)
    {
        await sfn.DeleteStateMachineAsync(new DeleteStateMachineRequest { StateMachineArn = arn });
    }

    Console.WriteLine();
    Console.WriteLine("Cleanup -> state machines deleted");
}

async Task<string> CreateAsync(string name, string definition)
{
    CreateStateMachineResponse created = await sfn.CreateStateMachineAsync(new CreateStateMachineRequest { Name = name, Definition = definition, RoleArn = roleArn });
    stateMachines.Add(created.StateMachineArn);
    return created.StateMachineArn;
}
