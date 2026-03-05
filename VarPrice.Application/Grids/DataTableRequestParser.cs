using Microsoft.AspNetCore.Http;

namespace VarPrice.Application.Grids;

public sealed class DataTableRequestParser : IDataTableRequestParser
{
    private const int DefaultPageLength = 25;
    private const int MaxPageLength = 200;

    public DataTableRequest Parse(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasFormContentType)
        {
            return new DataTableRequest
            {
                Draw = 0,
                Start = 0,
                Length = DefaultPageLength,
                SearchValue = null,
                OrderColumn = 0,
                OrderAscending = true
            };
        }

        var form = request.Form;
        var requestedLength = ParseInt(form["length"], DefaultPageLength);
        var length = requestedLength switch
        {
            < 1 => DefaultPageLength,
            > MaxPageLength => MaxPageLength,
            _ => requestedLength
        };

        var orderDirection = form["order[0][dir]"].ToString();

        return new DataTableRequest
        {
            Draw = ParseInt(form["draw"], 0),
            Start = Math.Max(ParseInt(form["start"], 0), 0),
            Length = length,
            SearchValue = NormalizeSearch(form["search[value]"]),
            OrderColumn = ParseInt(form["order[0][column]"], 0),
            OrderAscending = !string.Equals(orderDirection, "desc", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static int ParseInt(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
