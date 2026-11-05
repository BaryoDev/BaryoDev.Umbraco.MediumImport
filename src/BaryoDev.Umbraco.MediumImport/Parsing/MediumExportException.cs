namespace BaryoDev.Umbraco.MediumImport.Parsing;

/// <summary>The export cannot be read: a cap was breached, it is not a zip, an entry name is unsafe, or it holds no posts.</summary>
public sealed class MediumExportException : Exception
{
    public MediumExportException(string message) : base(message) { }
}
