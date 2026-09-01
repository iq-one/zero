namespace IQOne.Zero.Messaging.Exceptions;

/// <summary>
/// Base for expected service failures — the ones a handler raises deliberately rather than
/// the ones that indicate a defect.
/// </summary>
/// <remarks>
/// The framework attaches no HTTP status code to these. Translating a failure into a status
/// code and a response body is the transport's decision, made by the application's result
/// writer, because the mapping is part of a published contract rather than of the framework.
/// </remarks>
/// <param name="message">What went wrong, in terms the caller can act on.</param>
public class ServiceException(string message) : Exception(message);

/// <summary>The caller could not be identified.</summary>
/// <param name="message">What went wrong.</param>
public sealed class AuthenticationException(string message) : ServiceException(message);

/// <summary>The caller is known but not permitted to do this.</summary>
/// <param name="message">What went wrong.</param>
public sealed class AuthorizationException(string message) : ServiceException(message);

/// <summary>The requested data does not exist.</summary>
/// <param name="message">What was not found.</param>
public sealed class DataNotFoundException(string message) : ServiceException(message);

/// <summary>The request was well-formed but its contents are not acceptable.</summary>
/// <param name="message">Which value is wrong and what was expected.</param>
public sealed class ValidationException(string message) : ServiceException(message);
