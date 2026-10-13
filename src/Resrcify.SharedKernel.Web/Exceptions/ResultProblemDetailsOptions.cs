namespace Resrcify.SharedKernel.Web.Exceptions;

/// <summary>How <see cref="ResultExceptionHandler"/> answers an unhandled exception.</summary>
public sealed class ResultProblemDetailsOptions
{
    /// <summary>
    /// Whether the response shows the exception: its type and message as the error's message, and the whole
    /// exception (with its stack) as the problem's <c>detail</c>. <see langword="null"/> (the default): in the
    /// Development environment only. Elsewhere the response says only that something went wrong.
    /// </summary>
    public bool? IncludeExceptionDetails { get; set; }
}
