using XdkExplorer.Xbdm;

namespace XdkExplorer.Services;

public sealed class ConsoleConnectionResult
{
    private ConsoleConnectionResult(bool isSuccess, string target, string? name, string? address, string message)
    {
        IsSuccess = isSuccess;
        Target = target;
        Name = name;
        Address = address;
        Message = message;
    }

    public bool IsSuccess { get; }

    /// <summary>Name or IP as entered, later used to talk to the console.</summary>
    public string Target { get; }

    /// <summary>Name the console reports for itself.</summary>
    public string? Name { get; }

    public string? Address { get; }

    public string Message { get; }

    public static ConsoleConnectionResult Success(string target, string name, string address)
    {
        return new ConsoleConnectionResult(true, target, name, address, $"Connected · {name} · {address}");
    }

    public static ConsoleConnectionResult Failure(string target, string message)
    {
        return new ConsoleConnectionResult(false, target, null, null, message);
    }
}

public static class ConsoleConnectionTest
{
    public static async Task<ConsoleConnectionResult> RunAsync(XbdmWorker worker, string target)
    {
        try
        {
            var (address, name) = await worker.RunAsync(target, () => (XbdmCommands.ResolveAddress(), XbdmCommands.GetConsoleName()));

            return ConsoleConnectionResult.Success(target, string.IsNullOrEmpty(name) ? target : name, address.ToString());
        }
        catch (XbdmException)
        {
            return ConsoleConnectionResult.Failure(target,
                $"No answer from {target}. Check the name or IP address. The console has to be switched on and in the same network.");
        }
    }
}
