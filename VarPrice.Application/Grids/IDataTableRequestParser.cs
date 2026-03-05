using Microsoft.AspNetCore.Http;

namespace VarPrice.Application.Grids;

public interface IDataTableRequestParser
{
    DataTableRequest Parse(HttpRequest request);
}
