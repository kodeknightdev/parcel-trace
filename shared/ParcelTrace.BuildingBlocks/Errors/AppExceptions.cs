namespace ParcelTrace.BuildingBlocks.Errors;

public abstract class AppException(string message, int statusCode, string title) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}

public sealed class NotFoundException(string message) : AppException(message, 404, "Resource not found");

public sealed class ConflictException(string message) : AppException(message, 409, "Conflict");

public sealed class ForbiddenException(string message) : AppException(message, 403, "Forbidden");

public sealed class BadRequestException(string message) : AppException(message, 400, "Bad request");

public sealed class ServiceUnavailableException(string message) : AppException(message, 503, "Dependency unavailable");
