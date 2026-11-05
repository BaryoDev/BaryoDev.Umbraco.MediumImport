namespace BaryoDev.Umbraco.MediumImport.Parsing;

/// <summary>A post body nests elements deeper than the importer allows. The message is fixed and safe to show.</summary>
public sealed class HtmlTooDeepException : Exception
{
    public const string FixedMessage = "The post HTML is nested too deeply to import.";

    public HtmlTooDeepException() : base(FixedMessage) { }
}
