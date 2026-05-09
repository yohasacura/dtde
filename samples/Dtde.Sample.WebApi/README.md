# Dtde.Sample.WebApi — getting-started

The **smallest possible** DTDE sample. A minimal Web API with two entities:

- `Customer` — sharded by `Region` (property-based).
- `Contract` — temporal (bi-temporal validity via `ValidFrom`/`ValidTo`).

Use this sample to learn the basic shape of a DTDE-enabled application: how to
inherit `DtdeDbContext`, register `UseDtde`, and configure entities in
`OnModelCreating`. For specialised strategies, see the strategy-specific
samples (`Dtde.Samples.RegionSharding`, `Dtde.Samples.DateSharding`,
`Dtde.Samples.HashSharding`, `Dtde.Samples.MultiTenant`,
`Dtde.Samples.Combined`).

## Run

```bash
cd samples/Dtde.Sample.WebApi
dotnet run
```

Then open `http://localhost:5000/swagger` (or the URL printed on stdout).

## Key files to read

| File | What it shows |
|---|---|
| [`Program.cs`](Program.cs) | DTDE registration, shard definitions, OpenAPI setup. |
| [`Data/SampleDbContext.cs`](Data/SampleDbContext.cs) | `DtdeDbContext` subclass with `ShardBy` + `HasTemporalValidity`. |
| [`Controllers/CustomersController.cs`](Controllers/CustomersController.cs) | Standard EF Core CRUD — sharding is transparent. |
| [`Controllers/ContractsController.cs`](Controllers/ContractsController.cs) | Point-in-time queries with `ValidAt<T>(...)`. |
