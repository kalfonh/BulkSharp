using Microsoft.Extensions.Logging;

namespace BulkSharp.Dashboard;

internal static partial class LogMessages
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create bulk operation {OperationName}")]
    public static partial void CreateOperationFailed(this ILogger logger, Exception ex, string operationName);
}
