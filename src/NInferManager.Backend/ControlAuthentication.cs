using System.Security.Cryptography;
using System.Text;

namespace NInferManager.Backend;

public sealed class ControlAuthentication
{
    public const string Header = "X-NInferEZ-Control-Token";
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    private readonly byte[] _token;

    public ControlAuthentication()
    {
        var token = Environment.GetEnvironmentVariable("NINFEREZ_MANAGER_CONTROL_TOKEN");
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new InvalidOperationException("Launch the backend through NInferEZ Manager. A private control-session token is required.");
        _token = Encoding.UTF8.GetBytes(token);
        Environment.SetEnvironmentVariable("NINFEREZ_MANAGER_CONTROL_TOKEN", null);
    }

    public bool Authorized(string supplied) => CryptographicOperations.FixedTimeEquals(_token, Encoding.UTF8.GetBytes(supplied));
}
