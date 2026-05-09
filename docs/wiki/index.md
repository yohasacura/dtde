# Wiki

Reference documentation for advanced consumers and contributors. For
narrative tutorials, see the [guides](../guides/index.md) section.

| Document | What it covers |
|---|---|
| [Architecture](architecture.md) | The three-project layering, key abstractions, request flows. |
| [API reference](api-reference.md) | Public type catalogue: every extension method, fluent option, and contract. |
| [Configuration](configuration.md) | Every option on `DtdeOptionsBuilder`, JSON shard-config schema. |
| [Troubleshooting](troubleshooting.md) | Common errors and fixes. |
| [Classes reference](classes-reference.md) | Class-by-class API documentation generated from XML doc comments. |

## The three packages

| Package | When to reference |
|---|---|
| **`Dtde.EntityFramework`** | Application code. The only one you need most of the time. Pulls in `Dtde.Core` and `Dtde.Abstractions` transitively. |
| **`Dtde.Core`** | When implementing a custom sharding strategy or transaction log. |
| **`Dtde.Abstractions`** | When defining a contract that custom providers will implement (rare). |

## See also

- The runnable [samples](https://github.com/yohasacura/dtde/tree/main/samples) — eight Web API projects covering every feature.
- The [development plan](../development-plan/) — internal roadmap and architectural notes.
