using Microsoft.EntityFrameworkCore;
using VarPrice.Application.Grids;

namespace VarPrice.Web.Tests;

public sealed class DataTableQueryServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Applies_Filter_Order_And_Paging()
    {
        await using var db = CreateContext();
        db.Set<TestEntity>().AddRange(
            new TestEntity { Id = 1, Name = "alpha" },
            new TestEntity { Id = 2, Name = "bravo" },
            new TestEntity { Id = 3, Name = "alpine" },
            new TestEntity { Id = 4, Name = "zulu" });
        await db.SaveChangesAsync();

        IDataTableQueryService service = new DataTableQueryService();
        var request = new DataTableRequest
        {
            Draw = 5,
            Start = 0,
            Length = 2,
            SearchValue = "al",
            OrderColumn = 0,
            OrderAscending = false
        };

        var result = await service.ExecuteAsync(
            db.Set<TestEntity>().AsNoTracking(),
            request,
            entity => entity.Id,
            (query, search) => string.IsNullOrWhiteSpace(search)
                ? query
                : query.Where(x => x.Name.Contains(search)),
            (query, _, asc) => asc
                ? query.OrderBy(x => x.Id)
                : query.OrderByDescending(x => x.Id));

        Assert.Equal(5, result.Draw);
        Assert.Equal(4, result.RecordsTotal);
        Assert.Equal(2, result.RecordsFiltered);
        Assert.Equal([3, 1], result.Data);
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Items => Set<TestEntity>();
    }

    private sealed class TestEntity
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }
}
