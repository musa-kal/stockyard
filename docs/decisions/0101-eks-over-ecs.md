# 0101: Run the services on EKS rather than ECS

Status: accepted

## Context

Stockyard deploys four long-running .NET services (api, projections, notifier, exporter) and a one-shot migration. They need per-service AWS permissions, secrets injected at start, a health-checked api that scales on CPU, and a way to run the same topology on a laptop.

ECS on Fargate would cover the AWS side with less to operate: no node group, no add-ons, no cluster version upgrades, and task roles give per-service IAM directly. EKS costs a control plane fee per hour on top of the nodes and brings its own upgrade cadence.

## Decision

Use EKS with a managed node group, plain Kubernetes manifests, and kustomize overlays for local and AWS.

The reasons that tipped it:

* The same manifests run on Docker Desktop or kind with a local overlay. ECS task definitions have no local equivalent, so the local stack would only ever be the compose file and the deployment shape would go untested until it reached AWS.
* IRSA gives each service account its own role, which is the same least-privilege shape as ECS task roles.
* The Secrets Store CSI driver, HPA, PodDisruptionBudget, restricted Pod Security, and a Job for migrations are standard Kubernetes objects. Equivalent behaviour on ECS is spread across task definition fields, service auto scaling policies, and a separate run-task call for the migration.
* The manifests are portable to any other Kubernetes provider if the AWS account goes away.

## Consequences

* The control plane is billed for every hour the cluster exists, whether or not anything runs on it (see the cost table in docs/platform.md). The cluster is torn down between uses.
* Somebody has to upgrade the cluster one minor version at a time and keep the add-ons current. The add-ons are EKS managed to keep that small.
* Two extra components need installing after the cluster is up: the Secrets Store CSI driver and its AWS provider (docs/runbook.md, first deploy).
