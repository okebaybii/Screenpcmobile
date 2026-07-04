namespace PCToMobile.Models;

public sealed record MdnsService(string Name, string Type, string Endpoint)
{
    public string Host =>
        Endpoint.LastIndexOf(':') is var separator && separator > 0
            ? Endpoint[..separator]
            : Endpoint;
}
