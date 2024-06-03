namespace ParcelTrace.Frontend.Services;

public sealed record ApiResult<T>(T? Value, int StatusCode, string? Error, IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public bool Ok => StatusCode is >= 200 and < 300;
    public bool NotFound => StatusCode == 404;

    public static ApiResult<T> Success(T value, int status = 200) => new(value, status, null);
    public static ApiResult<T> Failure(int status, string error, IReadOnlyDictionary<string, string[]>? errors = null) => new(default, status, error, errors);
}
