# Operations & Runbook — Resrcify.SharedKernel

Operational, deployment, and production-debugging knowledge for this project — hard-won incident
findings that otherwise get lost in ad-hoc notes or misfiled into another project's session memory.
**Keep it here: in-repo, versioned, discoverable.** One `##` section per finding:
Symptom -> Root cause -> Fix -> Verification.

## A service using scatter-gather doesn't start on RabbitMQ 4.3

- **Symptom:** the service logs "Existing connection found to be CLOSED" every second for a minute, then fails to start
  ("Queue declaration for 'scatter.replies.…' failed"). The broker logs `Feature transient_nonexcl_queues is deprecated
  … not permitted anymore`.
- **Root cause:** the scatter-gather reply queue was transient and not exclusive, which RabbitMQ 4.3 refuses by default
  (4.1 accepted it).
- **Fix:** 4.0.0 declares it durable with a queue TTL (`x-expires`, 30 minutes): fixed in the package, nothing to do.
- **Verification:** the MessageBus integration tests run on `rabbitmq:4.3.4-management`, the fixtures' default.

## More than one RabbitMQ replica: quorum queues and the delivery limit

- **Symptom:** with several nodes (the RabbitMQ Cluster Operator's `replicas` above 1), stopping one node makes the
  queues it holds unavailable: publishes to them are rejected and their messages wait for the node. With quorum queues,
  a message can also disappear silently after 20 deliveries.
- **Root cause:** since RabbitMQ 4.0, classic queues are never replicated (mirroring is gone); only quorum queues are.
  Quorum queues drop a message after 20 deliveries by default, and a message handed back by a closed channel counts
  as a delivery (a nack with requeue doesn't). The bus hands messages back that way when a service stops, a
  rate-limited queue's health gate closes, or a connection drops. Measured on a 3-node 4.3.4 cluster.
- **Fix:** on the broker, once per cluster, before deploying services whose queues should be quorum (in Kubernetes,
  run each through `kubectl exec -n <namespace> <cluster>-server-0 -- `):
  1. Quorum as the default queue type of the vhost (a queue declared without a type gets it; exclusive ones, such as
     MassTransit's temporary bus queues, stay classic):
     `rabbitmqctl update_vhost_metadata / --default-queue-type quorum`
  2. No delivery limit on quorum queues:
     `rabbitmqctl set_policy --apply-to quorum_queues unlimited-delivery ".*" '{"delivery-limit":-1}'`
     A queue gets only one policy (the highest priority): a later policy matching these queues must repeat
     `"delivery-limit": -1`. A queue's own `x-delivery-limit` argument beats the policy. Don't put the argument on a
     classic queue: the broker refuses the declaration.
  3. Existing queues keep their type (it can't change). For each service: stop it, check its queues are empty
     (`rabbitmqctl list_queues name type messages`), delete them (`rabbitmqctl delete_queue <name>`), start it: they
     come back as quorum queues. A new service's queues are quorum from the start.

  4. **Optional: start at one replica, grow later.** Set the size new quorum queues start with on every node, in
     `advanced.config` (the operator's `spec.rabbitmq.advancedConfig`); a `rabbitmq.conf` line of that name stops the
     node from starting:
     `[{rabbit, [{quorum_cluster_size, 1}]}].`
     A one-replica quorum queue is a classic queue as far as availability goes: while its node is down, publishes to it
     are rejected and it can't be read; its messages are there when the node is back (measured side by side with a
     classic queue on the same node). It is still a quorum queue otherwise: the delivery limit applies (step 2 is still
     needed: without it a message went after 20 deliveries), and it can't be exclusive or non-durable. What it saves is
     step 3 later: going to three replicas is online, no queue deleted or emptied:
     `rabbitmq-queues grow rabbit@<node> all --vhost-pattern ".*" --queue-pattern ".*"` for each other node (measured:
     1 → 3 members, messages kept). Change `quorum_cluster_size` back (or remove it) so new queues start with three.
     Neither a policy `initial-cluster-size` nor `target-group-size` makes a new quorum queue smaller (measured).

  Both settings live in the cluster's own data: they survive restarts, not a cluster recreated from scratch. Declaring
  quorum queues in code instead works too (`x-queue-type: quorum` through an `IBusConfigurationStrategy`; MassTransit's
  `SetQuorumQueue()`), but every service would need it, and MassTransit's services as well.
- **Verification:** `rabbitmqctl environment` shows `{quorum_cluster_size,1}` when step 4 is used, and
  `rabbitmqctl list_queues name type members` one member per queue; `rabbitmqctl list_vhosts name default_queue_type` shows `quorum`; `rabbitmqctl list_policies`
  shows `unlimited-delivery`; `rabbitmq-queues check_if_node_is_quorum_critical` before stopping a node;
  `rabbitmqctl list_queues name type policy` shows the services' queues as `quorum` with the policy. On the 3-node test
  cluster, traffic through both buses survived stopping the node leading the queues, and a message handed back 30
  times by a closed channel stayed. While a node is down, a connection that was on it reconnects (about 2 s in the tests; up to
  18 s when the broker watcher's own connection was on that node, as it then waits to reconnect before restarting the
  consumers). Requests sent meanwhile wait and are answered late, within their timeout (before 4.0.0's fix they
  could throw; see the changelog). Under a request and an event every 250 ms, stopping the node holding the request,
  reply or event consumer lost nothing.

## Events in a service's error queue

- **Symptom:** `messagebus.messages.dead_lettered` rises (the `MessageBusMessagesDeadLettered` alert); a service's
  `<input queue>.error` queue holds messages.
- **Root cause:** an integration event's handler kept failing (5 tries in its place by default), so the bus moved the
  event aside rather than retry it forever. The message's `rbs2-error-details` header says which handler and why.
- **Fix:** find and fix the cause, then move the events back. Through the service's admin endpoints
  (`app.MapErrorQueueAdministration()`, under `/admin/error-queue`): `GET /` lists them with why each failed;
  `POST /retry` (or `POST /retry?type=<event>`) moves them back to be handled again, resuming at the handler that
  failed; `DELETE /messages/{id}` drops one that should never be handled. In code, `IErrorQueue` does the same.
- **Verification:** `GET /` answers `count: 0`; the handler's `messagebus.events.handled` counts `success` for them.
