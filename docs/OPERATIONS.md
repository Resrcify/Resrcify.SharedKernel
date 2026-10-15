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
- **Fix:** on the broker, not in the services: one setting covers every service, MassTransit's included. Declaring
  quorum queues in code works too (`x-queue-type: quorum` through an `IBusConfigurationStrategy`; MassTransit's
  `SetQuorumQueue()`), but every service would need it.
  1. **In the broker's config** (`rabbitmq.conf`; the RabbitMQ Cluster Operator's `spec.rabbitmq.additionalConfig`),
     so a cluster built from scratch has it too:

     ```
     default_queue_type = quorum
     quorum_queue.initial_cluster_size = 1
     default_policies.operator.delivery.vhost_pattern = .*
     default_policies.operator.delivery.queue_pattern = .*
     default_policies.operator.delivery.delivery_limit = 1000000
     ```

     - `default_queue_type`: a queue declared without a type is quorum (exclusive ones, such as MassTransit's
       temporary bus queues, stay classic).
     - `quorum_queue.initial_cluster_size = 1` (optional): new quorum queues start on one node. Such a queue is a
       classic queue as far as availability goes (while its node is down, publishes to it are rejected; its messages
       are there when the node is back), but going to three replicas is then online, no queue deleted or emptied:
       `rabbitmq-queues grow rabbit@<node> all --vhost-pattern ".*" --queue-pattern ".*"` for each other node
       (measured: 1 → 3 members, messages kept); then remove the line so new queues start on three. A policy's
       `initial-cluster-size` or `target-group-size` doesn't make a new queue smaller (measured).
     - The operator policy lifts the delivery limit. A config can't say "unlimited" (-1, it takes a positive number),
       so a limit that is never reached.

     These apply to vhosts the broker creates from now on, including `/` on a new cluster. Don't load a definitions
     file for them instead: with one, a new node creates no default vhost or user, so the operator's default user
     (the one the services connect with) is missing. Measured on 4.1.3; 4.3.4 has the same settings.
  2. **A broker that already has its vhost keeps the vhost's classic default**: switch it once, by hand. Once it is
     quorum, the broker refuses a service re-declaring one of its existing classic queues (`inequivalent arg
     'x-queue-type' ... received 'quorum' but current is 'classic'`; relaxed redeclaration checks don't help), so a
     service restarted before its queues are recreated fails to start. Do it in one window, every service on the bus
     stopped (and anything that restarts them, such as a GitOps sync or node reboots, paused); in Kubernetes run each
     command through `kubectl exec -n <namespace> <cluster>-server-0 -- `:
     1. `rabbitmqctl update_vhost_metadata / --default-queue-type quorum`
     2. `rabbitmqctl set_operator_policy delivery ".*" '{"delivery-limit":1000000}' --apply-to quorum_queues` (or a
        regular policy, which can say -1: `rabbitmqctl set_policy --apply-to quorum_queues unlimited-delivery ".*"
        '{"delivery-limit":-1}'`; a queue gets one regular policy, the highest priority, so a later one matching these
        queues must repeat the limit. Don't put `x-delivery-limit` on a classic queue: the broker refuses it.)
     3. Delete **every** classic durable queue on the vhost, not only the services' named ones: `rabbitmqctl
        list_queues name type durable messages`, then `rabbitmqctl delete_queue <name> --if-empty` for each
        `classic` one. That includes the bus's default `error` queue (a service without an input queue declares it),
        the queues of a service already running this package (a responder's request queues,
        `<service>.scatter.replies.*`), and MassTransit's. Then check that the list shows no `classic` queue before
        anything starts. If one is left, every service that declares it fails to start (`Queue declaration for
        'error' failed`, on the `x-queue-type` mismatch). A queue still holding messages loses them when it's
        deleted, so drain it first: stop its publishers and let its consumer empty it. This bus can't read
        MassTransit's messages, so a MassTransit queue must be empty before it goes.
     4. Start the subscribers first, and start the publishers only once the subscribers' queues are bound
        (`rabbitmqctl list_bindings`). An event published to a topic with no queue bound to it goes nowhere, and the
        outbox has already marked it processed.

     The Resrcify production cut-over (MassTransit to this bus in the same window) has its own full procedure,
     rehearsed on 2026-10-15 with production copies. It lives in Resrcify.Kubernetes,
     [`docs/OPERATIONS.md`, "RabbitMQ: quorum queues"](https://github.com/Resrcify/Resrcify.Kubernetes/blob/master/docs/OPERATIONS.md#rabbitmq-quorum-queues).
     Follow that one for production; these steps are the general rule.

- **Verification:** `rabbitmqctl environment` shows `{quorum_cluster_size,1}` with `initial_cluster_size = 1`, and
  `rabbitmqctl list_queues name type members` one member per queue; `rabbitmqctl list_vhosts name default_queue_type` shows `quorum`; `rabbitmqctl list_operator_policies`
  shows `delivery` (or `list_policies` your regular one); `rabbitmq-queues check_if_node_is_quorum_critical` before stopping a node;
  `rabbitmqctl list_queues name type operator_policy policy` shows the services' queues as `quorum` with the policy. On the 3-node test
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
