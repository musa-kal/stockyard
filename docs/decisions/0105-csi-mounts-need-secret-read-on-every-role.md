# 0105: Every IRSA role can read the connection string secret

Status: accepted

## Context

The access plan for the services gives the notifier "Secrets Manager `GetSecretValue` on the signing secret only", and gives the api, projections, and exporter no Secrets Manager access at all. The same plan delivers the connection string and the signing key to the pods through the Secrets Store CSI driver.

Those two statements cannot both hold. The AWS provider for the CSI driver does not have credentials of its own: it calls Secrets Manager with the IAM role of the pod that mounts the volume (IRSA here). A pod whose role cannot read `stockyard/<env>/db` never gets its volume mounted, the `stockyard-db` Kubernetes Secret is never synced, and the pod stays in `ContainerCreating`. All four services and the migrate job need the connection string, so all five pods mount that secret.

## Decision

Each IRSA role gets a second inline policy, `mount-db-secret`, allowing `secretsmanager:GetSecretValue` and `secretsmanager:DescribeSecret` on the connection string secret and nothing else (`infra/terraform/modules/eks/irsa.tf`). The notifier's role keeps its grant on the signing secret, with `DescribeSecret` added for the same reason. No role can read the master password secret RDS manages.

The application-level access is unchanged and still matches the plan: no service calls Secrets Manager itself, the notifier has no SQS permission, and the exporter can only put objects under the export prefix. The least privilege table in docs/platform.md lists the mount grants in their own column so the difference is visible.

## Consequences

* A compromised pod could read the connection string through the AWS API as well as from its own environment. That adds no exposure, since the same pod already holds the value in its environment.
* The alternative, giving the CSI driver's own service account a role and using it for every mount, would let any pod in the cluster that can create a SecretProviderClass read any secret that role can read. Per-pod roles keep the blast radius per workload.
* If the services later move to EKS Pod Identity, the same policies attach to the pod identity associations instead.
