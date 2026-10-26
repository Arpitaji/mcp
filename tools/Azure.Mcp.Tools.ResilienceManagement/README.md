# Resilience Management tools

<!-- cspell:ignore centraluseuap -->

## Current Drill, Goal, and Recovery contracts

Drill parent and child reads, mutations, actions, and their SDK polling requests use
the `2026-10-31-preview` Resilience Management contract. This keeps current Drill
properties such as monitoring configuration, lifecycle stage, `linkedJobIds`, and
reprotect readiness available to QCS instead of falling back to the older generated
SDK contract.

Goal Assignment create/get and the recommend-capacity, refresh-resources, and
update-resources actions use the same current contract. Goal responses preserve
`serviceLevelResources` and `errorDetails` through model mapping and source-generated
JSON serialization.

Recovery Plan, Recovery Resource, Recovery Job, action, and operation-status polling
requests also use `2026-10-31-preview` instead of the generated SDK's `2026-10-01`
default.

Recovery Job retry and resume retain normal retries for their prerequisite GET, then
submit the state-changing action through a separate client with automatic retries
disabled so a lost or transient response cannot replay the mutation.

Both policies retain the centralized `AZURE_RESILIENCY_ARM_ENDPOINT` endpoint and
optional `AZURE_RESILIENCY_ROUTING_LOCATION` header behavior. Non-idempotent
submissions disable automatic Azure SDK retries; callers must explicitly retry after
observing the latest service state.

The normal ARM SSRF allowlist admits only the shipped regional QCS hosts
`centraluseuap.management.azure.com` and `eastus2euap.management.azure.com` in
addition to the public ARM host. Other custom hosts, subdomains, schemes, paths,
queries, and fragments fail closed; routing never disables endpoint validation.

Drill monitoring updates are serialized once into a canonical PATCH. The
authoritative GET must return the exact requested Health Model ID or SLI selection,
or confirm an explicit clear, before the update is reported as successful.

## Drill lifecycle resource selection

`selectedResourceIds` are validated for the active Drill Run lifecycle stage before
submission:

- Forward fault-injection and failover stages require Drill Resource ARM IDs under
  the requested Drill.
- Reverse stages require qualified RO Recovery Resource ARM IDs under
  a Recovery Plan in the requested Service Group.
- Inverse, malformed, cross-scope, and mixed selections are rejected before an
  action is submitted.

Every retry reads the current Drill Run contract again and rebuilds its request body,
so parameters rejected for a prior stage are not retained automatically.

## Recovery-plan action closure

Recovery-plan failover and reprotect disable SDK retries, wait for the action LRO,
resolve the exact returned Recovery Job ID under the requested plan, and poll that
job to a documented terminal state. The result reports the terminal job status
rather than treating request acceptance as successful completion.
