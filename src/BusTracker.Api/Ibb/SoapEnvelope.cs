using System.Xml;
using System.Xml.Linq;

namespace BusTracker.Api.Ibb;

public static class SoapEnvelope
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Tempuri = "http://tempuri.org/";

    public static string SoapAction(string operation) => $"\"{Tempuri}{operation}\"";

    public static string Build(string operation, IReadOnlyDictionary<string, string> parameters)
    {
        var body = new XElement(Tempuri + operation,
            parameters.Select(p => new XElement(Tempuri + p.Key, p.Value)));
        var envelope = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XElement(Soap + "Body", body)));
        return envelope.Declaration + envelope.ToString(SaveOptions.DisableFormatting);
    }

    public static XElement ReadResult(string operation, string responseText)
    {
        var document = Parse(responseText);
        ThrowIfFault(document);
        return document.Descendants().FirstOrDefault(e => e.Name.LocalName == operation + "Result")
            ?? throw new IbbException($"{operation}: response has no result element");
    }

    private static XDocument Parse(string responseText)
    {
        try
        {
            return XDocument.Parse(responseText);
        }
        catch (XmlException)
        {
            throw new IbbException($"Response is not XML: {Truncate(responseText)}");
        }
    }

    private static void ThrowIfFault(XDocument document)
    {
        var fault = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
        if (fault is null) return;

        if (IsRateLimit(fault)) throw new IbbRateLimitedException();

        var faultString = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value;
        throw new IbbFaultException(faultString ?? "Unknown SOAP fault");
    }

    private static bool IsRateLimit(XElement fault) =>
        fault.Descendants()
            .Where(e => e.Name.LocalName == "policyResult")
            .Any(e => (string?)e.Attribute("status") is { } status
                      && status.Contains("Rate limit", StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];
}
