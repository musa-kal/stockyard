# Platform

How Stockyard runs on AWS: what Terraform creates, who can touch what, how the Kubernetes manifests are put together, what the pipeline does, how the warehouse layer reads the exports, and what it all costs while it exists. Operating procedures are in [runbook.md](runbook.md); the reasoning behind the larger choices is in the ADRs under [decisions/](decisions/) (0101 to 0105).

## AWS topology

Everything lives in one region (us-east-1) and one VPC spread over two availability zones. The services run on EKS in private subnets; SQL Server runs on RDS in the same private subnets and only accepts connections from the nodes. Outbound traffic from the nodes (to SQS, S3, Secrets Manager, ECR, and the EKS API) leaves through a single NAT gateway.

```mermaid
flowchart LR
    pipeline["Azure DevOps pipeline"]
    ecr[("ECR<br/>4 repositories")]
    sm["Secrets Manager<br/>db and signing secrets"]
    sqs["SQS<br/>stockyard-events<br/>stockyard-events-dlq"]
    s3[("S3<br/>export bucket")]
    cw["CloudWatch<br/>logs, metrics, alarms"]
    sns["SNS<br/>alarm topic"]
    dbt["dbt-duckdb<br/>s3 target"]

    subgraph vpc["VPC 10.40.0.0/16, two availability zones"]
        subgraph public["Public subnets"]
            nat["NAT gateway"]
        end
        subgraph private["Private subnets"]
            eks["EKS managed node group<br/>api, projections, notifier, exporter"]
            rds[("RDS SQL Server 2022 Express")]
        end
    end

    pipeline -->|"push images"| ecr
    pipeline -->|"terraform apply, kubectl apply"| eks
    eks -->|"1433"| rds
    eks -->|"via NAT"| nat
    nat --> sqs
    nat --> s3
    nat --> sm
    nat --> ecr
    eks --> cw
    cw --> sns
    s3 --> dbt
```

There is no load balancer or ingress: the api Service is `ClusterIP` and is reached with `kubectl port-forward`. Putting it on the internet would mean the AWS Load Balancer Controller, an Ingress, a certificate, and authentication in front of the API, none of which the project needs yet.

## Terraform layout

`infra/terraform` is one root module that wires seven child modules together. State is in S3 with the native S3 lockfile (`use_lockfile = true`); the bucket, key, and region are passed at `terraform init` so nothing account specific is committed.

| Path | What it creates |
| ----- | ----- |
| `versions.tf`, `providers.tf`, `backend.tf` | Terraform 1.10 or later, AWS provider 6.x with default tags, partial S3 backend |
| `main.tf` | Availability zone lookup, naming, and the module calls |
| `secrets.tf` | Secrets Manager secrets for `ConnectionStrings__Stockyard` and `Notifier__SigningSecret` (containers only; values are written out of band) |
| `variables.tf`, `example.tfvars` | Every input with a description, a default where one is safe, and validation |
| `outputs.tf` | Queue URLs, bucket, ECR URLs, IRSA role ARNs, secret names, and `k8s_aws_env` (the aws overlay's env file) |
| `modules/network` | VPC, public and private subnets per zone with the EKS discovery tags, internet gateway, one NAT gateway, route tables |
| `modules/eks` | Cluster role and control plane (API authentication mode, control plane logs with retention), managed node group on AL2023, core add-ons (VPC CNI with network policy enforcement, kube-proxy, CoreDNS), access entries for admins, and `irsa.tf`: the OIDC provider and one role per service account |
| `modules/rds` | DB subnet group, security group open to the cluster security group only, SQL Server Express instance with encrypted gp3 storage and an RDS managed master password |
| `modules/messaging` | `stockyard-events` and `stockyard-events-dlq` (redrive after 5 receives, only the events queue may redrive into the DLQ, SSE on both) |
| `modules/storage` | Export bucket: public access blocked, bucket owner enforced, SSE, TLS-only policy, exports expire after `export_retention_days` |
| `modules/registry` | Four ECR repositories with immutable tags, scan on push, and a lifecycle policy |
| `modules/observability` | SNS alarm topic (optional email), alarms on DLQ depth, events backlog age, database CPU and free storage, and Container Insights through the EKS add-on with its own IRSA role |
| `tests/variables.tftest.hcl` | `terraform test` against a mocked AWS provider: a default plan and the variable validations |

Checks that run without an AWS account:

```
terraform -chdir=infra/terraform fmt -check -recursive
terraform -chdir=infra/terraform init -backend=false
terraform -chdir=infra/terraform validate
terraform -chdir=infra/terraform test
```

`.terraform.lock.hcl` is committed with hashes for linux_amd64, windows_amd64, and darwin_arm64 so the pipeline agent and a laptop resolve the same provider build.

## Least privilege

Each Kubernetes service account has its own IAM role (IRSA), trusted only by `system:serviceaccount:stockyard:<name>`. The table is kept in step with `infra/terraform/modules/eks/irsa.tf` by hand, so change both together.

| Service account | What the service does with AWS | Granted for the service | Granted for the CSI secret mount (ADR 0105) |
| ----- | ----- | ----- | ----- |
| `stockyard-api` (also runs the migrate job) | Relays outbox rows to the queue; replays the DLQ back onto it; reads the pending count for `/stock/reconcile`; writes on-demand exports | `sqs:SendMessage`, `sqs:GetQueueAttributes` on `stockyard-events`; `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:GetQueueAttributes` on `stockyard-events-dlq`; `s3:PutObject` on `<bucket>/exports/*` | `secretsmanager:GetSecretValue`, `DescribeSecret` on the db secret |
| `stockyard-projections` | Only consumer of the events queue | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility`, `sqs:GetQueueAttributes` on `stockyard-events` | same, db secret |
| `stockyard-notifier` | Signs and sends webhooks from `WebhookDelivery` rows; never reads the queue (ADR 0004) | none beyond the mounts | db secret, and `GetSecretValue`, `DescribeSecret` on the signing secret |
| `stockyard-exporter` | Writes the nightly Parquet files; `StockReconciled` goes out through the outbox and the api relay | `s3:PutObject` on `<bucket>/exports/*` | same, db secret |
| `amazon-cloudwatch/cloudwatch-agent` | Container Insights metrics and logs | `CloudWatchAgentServerPolicy` | none |

What no workload can do: read the RDS master password secret, touch the DLQ (except the api's replay), read from the export bucket, delete anything in S3, or call any AWS API outside those listed. The nodes' own role has only the three EKS worker policies (worker node, CNI, ECR pull only).

Other boundaries:

* SQL Server accepts 1433 only from the EKS cluster security group, and has no public address.
* Cluster access is through EKS access entries for the principals in `cluster_admin_principal_arns` (the pipeline role and an admin role); the cluster creator does not get implicit admin.
* The `stockyard` namespace enforces the restricted Pod Security Standard, and a default deny NetworkPolicy leaves only port 8080 on the api pods open.

## Kubernetes

`deploy/k8s` is plain manifests with kustomize: a base and two overlays.

| Layer | Contents |
| ----- | ----- |
| `base` | Namespace `stockyard` (restricted Pod Security, enforce, audit, and warn); the `stockyard-config` ConfigMap generated from `config.env`; one ServiceAccount and Deployment per image (`stockyard-api`, `stockyard-projections`, `stockyard-notifier`, `stockyard-exporter`); the api Service, HPA (CPU 70 %, 2 to 6 pods), and PodDisruptionBudget (one pod always up); the `stockyard-migrate` Job; NetworkPolicies |
| `overlays/local` | Docker Desktop or kind. Images tagged `local`, plain Secrets with the compose values, LocalStack and SQL Server reached through `host.docker.internal`, LocalStack credentials, one api pod |
| `overlays/aws` | EKS. ECR image names, IRSA role annotations, one SecretProviderClass per secret, CSI volumes mounted so the Secrets sync, `aws.env` with the real queue URLs and export destination (rendered from Terraform outputs at deploy time) |

Every pod runs as the image's non-root `app` user (UID 1654) with `runAsNonRoot`, the `RuntimeDefault` seccomp profile, no privilege escalation, all capabilities dropped, and a read-only root filesystem with an `emptyDir` at `/tmp`. Service account tokens are not mounted; IRSA injects its own projected token. Every container has requests and limits.

The api has a liveness probe on `/healthz` (no dependencies, so a database outage never restarts it) and a readiness probe on `/readyz` (checks SQL Server). The workers listen on nothing and have no probes. The exporter uses the `Recreate` strategy so a rollout never runs two schedulers at 02:00 UTC.

Configuration reaches the containers with `envFrom`: the ConfigMap `stockyard-config` and the Secrets `stockyard-db` (`ConnectionStrings__Stockyard`) and, for the notifier, `stockyard-signing` (`Notifier__SigningSecret`). On AWS those Secrets are created by the Secrets Store CSI driver from Secrets Manager when a pod mounts its SecretProviderClass; locally they are plain Secrets. `Messaging__ServiceUrl` and `Exporter__ServiceUrl` point at LocalStack in the local overlay and are empty on AWS, where the SDK uses the regional endpoints.

Migrations run as the Job `stockyard-migrate`: the api image with `args: ["migrate"]` under the api's service account. A Job's pod template is immutable, so a deploy deletes the previous Job, applies the new one, waits for it to complete, and only then rolls the Deployments.

Render either overlay without a cluster:

```
kubectl kustomize deploy/k8s/overlays/local
kubectl kustomize deploy/k8s/overlays/aws
```

## Pipeline

`azure-pipelines.yml` with step templates in `.azure-pipelines/steps/`. Its header lists the extension, service connection, variable group entries, and environment it needs.

| Stage | Runs on | Jobs | Template |
| ----- | ----- | ----- | ----- |
| Build | every PR and push to main | Restore (NuGet cache keyed on `Directory.Packages.props`), build with `-warnaserror`, test on Microsoft.Testing.Platform with the xunit TRX report, publish results | `dotnet.yml` |
| Build | every PR and push to main | Platform checks in a parallel job: `terraform fmt -check`, `init -backend=false`, `validate`, and `test`; both kustomize overlays rendered; `dbt build` against freshly generated sample exports | `terraform.yml` (validate) |
| Package | main only | Four matrix jobs in parallel, one per image: `docker build` from the repository root, Trivy scan failing on fixable HIGH or CRITICAL, push to ECR tagged with the commit SHA | `docker-ecr.yml` |
| Deploy | main only | `plan`: Terraform plan into a file, published with a readable copy. `apply`: deployment job on the `stockyard-aws` environment (approval check), applies that exact plan, renders the aws overlay with the account, environment, and image tag, runs the migration Job, then rolls the Deployments | `terraform.yml`, `kubectl.yml` |

The pipeline has not been run against a real Azure DevOps organisation from this repository, so there are no recorded run durations. Validating it means creating the project, the service connection, variable group, and environment listed in the header, and queuing a run on main.

## Warehouse layer

`pipeline/dbt` is a dbt project on the `dbt-duckdb` adapter (ADR 0104). It reads the Exporter's Parquet files straight from their folders:

```
<root>/orders/<yyyy>/<MM>/<dd>/order_lines.parquet
<root>/movements/<yyyy>/<MM>/<dd>/stock_movements.parquet
```

where `<root>` is `s3://<export bucket>/exports` for the `s3` target and `pipeline/dbt/sample_data` for the `local` target.

| Layer | Models | Notes |
| ----- | ----- | ----- |
| Sources | `exports.order_lines`, `exports.stock_movements` | `read_parquet` over the day folders with `filename = true` |
| Staging (views) | `stg_order_lines`, `stg_stock_movements` | Upper-cased codes, UTC timestamps, export day from the folder path; an order line exported on several days keeps its latest export |
| Seed | `movement_kinds` | Kind name, the code the application stores, and the direction stock should move |
| Marts (tables) | `dim_item`, `dim_supplier`, `dim_job_site`, `dim_date`, `fct_purchase_order_lines`, `fct_stock_movements`, `fct_daily_stock_balance` | Star schema keyed on the natural codes the API uses |

```mermaid
erDiagram
    dim_supplier ||--o{ fct_purchase_order_lines : "supplier_code"
    dim_job_site ||--o{ fct_purchase_order_lines : "job_site_code"
    dim_item ||--o{ fct_purchase_order_lines : "item_sku"
    dim_date ||--o{ fct_purchase_order_lines : "posted_on"
    dim_job_site ||--o{ fct_stock_movements : "job_site_code"
    dim_item ||--o{ fct_stock_movements : "item_sku"
    dim_date ||--o{ fct_stock_movements : "occurred_on"
    movement_kinds ||--o{ fct_stock_movements : "kind"
    dim_item ||--o{ fct_daily_stock_balance : "item_sku"
    dim_job_site ||--o{ fct_daily_stock_balance : "job_site_code"
    dim_date ||--o{ fct_daily_stock_balance : "date_day"

    fct_purchase_order_lines {
        varchar order_line_key PK
        varchar order_number
        int line_number
        double quantity_ordered
        double quantity_received
        double quantity_outstanding
        double unit_cost
        double line_total
    }
    fct_stock_movements {
        bigint movement_id PK
        varchar kind
        tinyint kind_code
        double quantity
        varchar reference
        timestamp occurred_at
    }
    fct_daily_stock_balance {
        varchar item_sku
        varchar job_site_code
        date date_day
        double net_quantity
        double closing_quantity
    }
    dim_item {
        varchar item_sku PK
    }
    dim_supplier {
        varchar supplier_code PK
    }
    dim_job_site {
        varchar job_site_code PK
        boolean is_warehouse
    }
    dim_date {
        date date_day PK
    }
    movement_kinds {
        varchar kind PK
        tinyint kind_code
        varchar direction
    }
```

Tests: `unique` and `not_null` on every key, `relationships` from each fact to its dimensions, `accepted_values` on movement kinds and directions, the generic `non_negative` test (`tests/generic/non_negative.sql`) on quantities and costs, and singular tests that check line totals equal quantity times cost, ledger receipts reconcile with received quantities, and no site closes a day with negative stock.

Build it locally against two generated days:

```
py -3.13 -m venv pipeline/dbt/.venv
pipeline/dbt/.venv/Scripts/pip install -r pipeline/dbt/requirements.txt
pipeline/dbt/.venv/Scripts/python pipeline/dbt/scripts/make_sample_parquet.py
pipeline/dbt/.venv/Scripts/dbt build --project-dir pipeline/dbt --profiles-dir pipeline/dbt
```

On the sample set that build ends `PASS=65 WARN=0 ERROR=0 SKIP=0`. `dbt docs generate` followed by `dbt docs serve` shows the lineage:

![dbt docs lineage graph: the Parquet sources exports.order_lines and exports.stock_movements feed stg_order_lines and stg_stock_movements, which feed dim_date, dim_item, dim_job_site, dim_supplier, fct_purchase_order_lines and fct_stock_movements; the movement_kinds seed joins fct_stock_movements; fct_daily_stock_balance builds on dim_date and fct_stock_movements; the four singular tests hang off the facts.](images/dbt-lineage.png)

(`bin/` instead of `Scripts/` outside Windows.) Against the bucket, with AWS credentials in the environment:

```
STOCKYARD_EXPORT_BUCKET=<export_bucket output> pipeline/dbt/.venv/bin/dbt build --project-dir pipeline/dbt --profiles-dir pipeline/dbt --target s3
```

## Load tests

`perf/k6` holds two k6 scripts run through the `grafana/k6` image: `post-order.js` drives the write path (create an order, add a line, post it with an `Idempotency-Key`) and `stock-on-hand.js` the read path over `dbo.vw_StockOnHand`. Each carries its pass thresholds; [perf/k6/README.md](../perf/k6/README.md) has the load shapes and how to point them at the compose stack or at EKS through a port forward. No run against a deployed stack is recorded here, so there are no latency figures for AWS yet: take them with the commands in that README after a deployment.

## Cost

On-demand list prices for us-east-1, looked up on 2026-10-24 from the AWS Price List offer files (the machine-readable source behind the pricing pages; each offer file's own publication date is in the source column). Monthly figures use 730 hours and the defaults in `variables.tf`. Nothing here was measured from a bill: no deployment has been run from this repository yet, so the real figure is "measured after deployment" from Cost Explorer (`aws ce get-cost-and-usage` filtered on the `Project = stockyard` tag).

| Item | Quantity at the defaults | List price | Per month | Source |
| ----- | ----- | ----- | ----- | ----- |
| EKS control plane | 1 cluster, standard support | 0.10 USD per cluster hour | 73.00 | AmazonEKS offer, published 2026-09-28: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonEKS/current/us-east-1/index.json (page: https://aws.amazon.com/eks/pricing/) |
| EC2 nodes | 2 x t3.medium, Linux | 0.0416 USD per instance hour | 60.74 | EC2 on-demand data behind https://aws.amazon.com/ec2/pricing/on-demand/, published 2026-10-23: https://b0.p.awsstatic.com/pricing/2.0/meteredUnitMaps/ec2/USD/current/ec2-ondemand-without-sec-sel/US%20East%20(N.%20Virginia)/Linux/index.json |
| EBS for node root volumes | 2 x 30 GiB gp3 | 0.08 USD per GB-month | 4.80 | AmazonEC2 offer, published 2026-10-23: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonEC2/current/us-east-1/index.csv (page: https://aws.amazon.com/ebs/pricing/) |
| NAT gateway | 1 gateway | 0.045 USD per hour, plus 0.045 USD per GB processed | 32.85 plus traffic | AmazonEC2 offer as above (page: https://aws.amazon.com/vpc/pricing/) |
| Public IPv4 address | 1 (the NAT gateway's Elastic IP) | 0.005 USD per hour | 3.65 | AmazonVPC offer, published 2026-10-16: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonVPC/current/us-east-1/index.json |
| RDS SQL Server Express | 1 x db.t3.small, single-AZ, license included | 0.044 USD per instance hour | 32.12 | AmazonRDS offer, published 2026-10-20: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonRDS/current/us-east-1/index.json (page: https://aws.amazon.com/rds/sqlserver/pricing/) |
| RDS storage | 20 GiB gp3 | 0.115 USD per GB-month | 2.30 | AmazonRDS offer as above |
| Secrets Manager | 3 secrets (db, signing, and the RDS managed master password) | 0.40 USD per secret-month, 0.05 USD per 10,000 API calls | 1.20 plus calls | AWSSecretsManager offer, published 2026-09-11: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AWSSecretsManager/current/us-east-1/index.json |
| CloudWatch alarms | 4 standard resolution alarms | 0.10 USD per alarm-month | 0.40 | AmazonCloudWatch offer, published 2026-10-07: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonCloudWatch/current/us-east-1/index.json |
| CloudWatch Logs | control plane and container logs | 0.50 USD per GB ingested, 0.03 USD per GB-month stored | usage | AmazonCloudWatch offer as above |
| SQS | standard queue requests | first 1,000,000 requests a month free, then 0.40 USD per million | usage | AWSQueueService offer, published 2026-09-11: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AWSQueueService/current/us-east-1/index.json |
| S3 exports | Parquet files, a few MB a day | 0.023 USD per GB-month, 0.005 USD per 1,000 PUT requests | usage | AmazonS3 offer, published 2026-10-15: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonS3/current/us-east-1/index.json |
| ECR storage | up to 20 images per repository | 0.10 USD per GB-month | usage | AmazonECR offer, published 2026-09-11: https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonECR/current/us-east-1/index.json |
| Container Insights (EKS add-on) | per observation | not retrieved | not retrieved | check https://aws.amazon.com/cloudwatch/pricing/ (Container Insights section) |
| EC2 T3 unlimited CPU credits | only when nodes run above baseline | not retrieved | not retrieved | check https://aws.amazon.com/ec2/pricing/on-demand/ (T2/T3/T4g unlimited mode) |
| Data transfer out to the internet | webhook calls to suppliers | not retrieved | not retrieved | check https://aws.amazon.com/ec2/pricing/on-demand/ (data transfer) |

The fixed part, everything billed by the hour or the month whether or not a request is served, comes to about 211.06 USD a month, or about 0.28 USD an hour of which 0.10 is the EKS control plane, 0.083 the two nodes, 0.05 the NAT gateway and its address, and 0.044 the database. That is why the stack is torn down between uses ([runbook.md](runbook.md#tear-down)). The usage-based rows are small at this project's volume but were not estimated.
