using Microsoft.AspNetCore.Mvc;

using VarPrice.Application.Grids;

namespace VarPrice.Web.Infrastructure.DataTables;

public static class DataTableResults
{
    public static JsonResult Empty(int draw)
    {
        var response = new DataTableResponse<object>
        {
            Draw = draw,
            RecordsTotal = 0,
            RecordsFiltered = 0,
            Data = []
        };

        return new JsonResult(response);
    }
}
