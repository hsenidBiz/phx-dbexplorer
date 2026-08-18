namespace PhxDbExplorer.IntegrationTests;

// One container per collection rather than per test class — the schema and data tests
// share a single seeded database instead of starting a second one each.

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
