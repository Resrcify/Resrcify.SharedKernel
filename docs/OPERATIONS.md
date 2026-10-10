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

  Both settings live in the cluster's own data: they survive restarts, not a cluster recreated from scratch. Declaring
  quorum queues in code instead works too (`x-queue-type: quorum` through an `IBusConfigurationStrategy`; MassTransit's
  `SetQuorumQueue()`), but every service would need it, and MassTransit's services as well.
- **Verification:** `rabbitmqctl list_vhosts name default_queue_type` shows `quorum`; `rabbitmqctl list_policies`
  shows `unlimited-delivery`; `rabbitmq-queues check_if_node_is_quorum_critical` before stopping a node;
  `rabbitmqctl list_queues name type policy` shows the services' queues as `quorum` with the policy. On the 3-node test
  cluster, traffic through both buses survived stopping the node leading the queues, and a message handed back 30
  times by a closed channel stayed. While a node is down, a connection that was on it reconnects; the service's own bus
  can take up to a minute (one direct request with a 30 s timeout went unanswered in one test run; outbox-driven
  requests are retried).
