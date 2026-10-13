# Resrcify.SharedKernel.UnitOfWork.Web

Admin endpoints for the outbox of `Resrcify.SharedKernel.UnitOfWork`, so an operator never needs hand-written SQL to
see what is stuck or to try it again.

```csharp
app.MapOutboxAdministration<AppDbContext>()                    // under /admin/outbox
    .RequireAuthorization(policy => policy.RequireRole("Admin"));
```

| Endpoint | What it does |
|---|---|
| `GET /admin/outbox` | Messages waiting, retrying and given up, in all and per event type (with its lane). |
| `GET /admin/outbox/given-up?type=&take=` | The messages that gave up, most recent first (50 by default, at most 500). |
| `GET /admin/outbox/messages/{id}` | A message with its content, where it stands and its last error. |
| `POST /admin/outbox/messages/{id}/retry` | Tries a message that gave up again (204), or 404 when it didn't give up. |
| `POST /admin/outbox/given-up/retry?type=` | Tries every message that gave up (of one type when given) again. |

The endpoints require an authenticated caller on their own; add the admin role (or policy) on the returned group.
Messages hold event content: never open these endpoints to everyone. A retried message starts from its first try,
keeps its last error until the next one, and the outbox is woken so it is handled at once.

The same operations are available in code as `OutboxAdministration<TDbContext>` (scoped, registered by
`AddOutboxProcessing` and `AddOutboxLanes`), for a service that exposes them another way.
