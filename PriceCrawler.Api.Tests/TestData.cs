using Npgsql;

namespace PriceCrawler.Api.Tests;

/// <summary>
/// A small catalog for the integration tests. Store "varus": category Овочі (1) with child
/// Картопля (2), five products; one inactive product and one product never observed. Store
/// "other": one category and one product.
/// </summary>
internal static class TestData
{
    public static async Task SeedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(Sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private const string Sql = """
        insert into category (id, source, external_id, parent_id, name, url, sort_order, first_seen_at, last_seen_at)
        overriding system value values
            (1, 'varus', 'ovochi', null, 'Овочі', 'https://varus.ua/ovochi', 1, now(), now()),
            (2, 'varus', 'kartoplia', 1, 'Картопля', 'https://varus.ua/kartoplia', 1, now(), now()),
            (3, 'varus', 'molochni', null, 'Молочні', 'https://varus.ua/molochni', 2, now(), now()),
            (4, 'other', 'fruits', null, 'Fruits', 'https://other.example/fruits', 1, now(), now());

        insert into product_catalog (id, source, url, normalized_url, first_discovered_at, last_discovered_at, is_active)
        overriding system value values
            (11, 'varus', 'https://varus.ua/kartoplia-bila', 'https://varus.ua/kartoplia-bila', now(), now(), true),
            (12, 'varus', 'https://varus.ua/kartoplia-rozheva', 'https://varus.ua/kartoplia-rozheva', now(), now(), true),
            (13, 'varus', 'https://varus.ua/morkva', 'https://varus.ua/morkva', now(), now(), true),
            (14, 'varus', 'https://varus.ua/moloko-25', 'https://varus.ua/moloko-25', now(), now(), true),
            (15, 'varus', 'https://varus.ua/moloko_100', 'https://varus.ua/moloko_100', now(), now(), true),
            (16, 'varus', 'https://varus.ua/staryi', 'https://varus.ua/staryi', now(), now(), false),
            (17, 'varus', 'https://varus.ua/never-seen', 'https://varus.ua/never-seen', now(), now(), true),
            (18, 'other', 'https://other.example/apple', 'https://other.example/apple', now(), now(), true);

        insert into product_catalog_category (product_catalog_id, category_id, first_seen_at, last_seen_at) values
            (11, 2, now(), now()), (12, 2, now(), now()), (13, 1, now(), now()),
            (14, 3, now(), now()), (15, 3, now(), now()), (18, 4, now(), now());

        insert into product (id, external_id, name, url, pack_value, pack_unit, created_at)
        overriding system value values
            (101, '1001', 'Картопля біла', 'https://varus.ua/kartoplia-bila', 1, 'кг', now()),
            (102, '1002', 'Картопля рожева', 'https://varus.ua/kartoplia-rozheva', 1, 'кг', now()),
            (103, '1003', 'Морква', 'https://varus.ua/morkva', null, null, now()),
            (104, '1004', 'Молоко 2,5%', 'https://varus.ua/moloko-25', 0.9, 'л', now()),
            (105, '1005', 'Молоко_100 пробне', 'https://varus.ua/moloko_100', 1, 'л', now()),
            (106, '1006', 'Старий товар', 'https://varus.ua/staryi', null, null, now()),
            (107, '2001', 'Apple', 'https://other.example/apple', null, null, now());

        insert into crawler_run (id, status, source) values (1, 'success', 'test'), (2, 'success', 'test');

        insert into price_snapshot (id, run_id, product_id, captured_at, price, old_price, promo_flag, in_stock) values
            (1001, 1, 101, '2026-11-10T08:00:00Z', 30.00, null, false, true),
            (1002, 2, 101, '2026-11-12T08:00:00Z', 25.50, 30.00, true, true),
            (1003, 2, 102, '2026-11-12T08:00:00Z', 34.90, null, false, false),
            (1004, 2, 103, '2026-11-12T08:00:00Z', null, null, false, false),
            (1005, 2, 104, '2026-11-12T08:00:00Z', 48.20, 52.00, true, true),
            (1006, 2, 105, '2026-11-12T08:00:00Z', 61.00, null, false, true),
            (1007, 2, 106, '2026-11-12T08:00:00Z', 10.00, null, false, true),
            (1008, 2, 107, '2026-11-12T08:00:00Z', 5.00, null, false, true);
        """;
}
