using System.Text.Json;
using Yukkuri.Services;

try
{
    var request = JsonSerializer.Deserialize<AquesBridgeRequest>(Console.ReadLine() ?? "")
        ?? throw new InvalidOperationException("未收到合成请求。");
    Console.WriteLine(Convert.ToBase64String(AquesTalkService.Synthesize(request)));
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
}

