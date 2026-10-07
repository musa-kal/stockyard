# Runbook

Procedures for the AWS deployment. Commands assume the repository root as the working directory, `us-east-1`, the `dev` environment, and AWS credentials for an admin role that is listed in `cluster_admin_principal_arns`. Replace `dev` where your environment differs. Background on the moving parts is in [platform.md](platform.md).

Tools on the operator's machine: AWS CLI v2, Terraform 1.10 or later, kubectl, Helm 3, and `jq`.

## First deploy

1. Create the state bucket once, outside Terraform:

   ```
   aws s3api create-bucket --bucket <state bucket> --region us-east-1
   aws s3api put-bucket-versioning --bucket <state bucket> --versioning-configuration Status=Enabled
   aws s3api put-public-access-block --bucket <state bucket> \
     --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
   ```

2. Copy `infra/terraform/example.tfvars` to `infra/terraform/dev.tfvars` (ignored by git) and set `cluster_admin_principal_arns`, `cluster_endpoint_public_access_cidrs`, and optionally `alarm_email`.

3. Initialise, plan, and apply:

   ```
   cd infra/terraform
   terraform init -backend-config="bucket=<state bucket>" -backend-config="key=stockyard/dev.tfstate" -backend-config="region=us-east-1"
   terraform plan -var-file=dev.tfvars -out=tfplan
   terraform apply tfplan
   ```

   The EKS control plane and the RDS instance take the longest. If `alarm_email` is set, confirm the subscription email AWS sends or alarms go nowhere.

4. Point kubectl at the cluster:

   ```
   aws eks update-kubeconfig --name "$(terraform output -raw cluster_name)" --region us-east-1
   ```

5. Install the Secrets Store CSI driver with secret syncing, and its AWS provider:

   ```
   helm repo add secrets-store-csi-driver https://kubernetes-sigs.github.io/secrets-store-csi-driver/charts
   helm repo add aws-secrets-manager https://aws.github.io/secrets-store-csi-driver-provider-aws
   helm install csi-secrets-store secrets-store-csi-driver/secrets-store-csi-driver \
     --namespace kube-system --set syncSecret.enabled=true --set enableSecretRotation=true
   helm install secrets-provider-aws aws-secrets-manager/secrets-store-csi-driver-provider-aws \
     --namespace kube-system
   ```

6. Write the two application secrets. The connection string uses the master login whose password RDS generated and keeps in its own secret:

   ```
   host="$(terraform output -raw db_endpoint)"
   password="$(aws secretsmanager get-secret-value --secret-id "$(terraform output -raw db_master_user_secret_arn)" \
     --query SecretString --output text | jq -r .password)"
   aws secretsmanager put-secret-value --secret-id "$(terraform output -raw db_secret_name)" \
     --secret-string "$(jq -cn --arg cs "Server=$host,1433;Database=Stockyard;User Id=stockyard_admin;Password=$password;Encrypt=True;TrustServerCertificate=True" '{ConnectionStrings__Stockyard: $cs}')"
   aws secretsmanager put-secret-value --secret-id "$(terraform output -raw signing_secret_name)" \
     --secret-string "$(jq -cn --arg s "$(openssl rand -hex 32)" '{Notifier__SigningSecret: $s}')"
   unset password
   ```

   Give each supplier that receives webhooks the signing key through a channel you trust; they need it to verify `X-Stockyard-Signature`.

7. Push the images and roll out. The normal path is the pipeline: push to main, let Package build and push the four images, read the plan, and approve the Deploy stage. The apply job renders the aws overlay, runs `stockyard-migrate`, and rolls the Deployments (`.azure-pipelines/steps/kubectl.yml` has the exact commands if you need to do it by hand).

8. Check it:

   ```
   kubectl get pods --namespace stockyard
   kubectl logs job/stockyard-migrate --namespace stockyard
   kubectl port-forward --namespace stockyard service/stockyard-api 8080:80
   curl -fsS http://localhost:8080/readyz
   ```

   A pod stuck in `ContainerCreating` with a `FailedMount` event almost always means its role cannot read the secret it mounts, or the secret has no value yet (step 6).

## Rotate the signing secret

The signature header carries no key id, so for the length of the swap a supplier checking with the old key rejects webhooks signed with the new one. Failed deliveries are retried (doubling from 1 s up to a 10 minute cap, 8 attempts), so a swap that finishes within a few minutes loses nothing.

1. Generate the new key and agree a time with the suppliers that verify signatures.
2. Write it:

   ```
   cd infra/terraform
   aws secretsmanager put-secret-value --secret-id "$(terraform output -raw signing_secret_name)" \
     --secret-string "$(jq -cn --arg s "$(openssl rand -hex 32)" '{Notifier__SigningSecret: $s}')"
   ```

3. The CSI driver refreshes the synced `stockyard-signing` Secret on its rotation poll, but the notifier reads environment variables only at start, so restart it:

   ```
   kubectl rollout restart deployment/stockyard-notifier --namespace stockyard
   kubectl rollout status deployment/stockyard-notifier --namespace stockyard
   ```

4. Hand the new key to the suppliers. Deliveries that failed in between show `LastStatusCode` 401 or similar on their `WebhookDelivery` row; any that ran out of attempts can be sent again with `POST /api/v1/webhooks/{deliveryId}/replay`.

## Rotate the master password

RDS owns the master password (`manage_master_user_password`), so rotation happens in Secrets Manager and the application secret has to follow it.

1. Rotate:

   ```
   cd infra/terraform
   aws secretsmanager rotate-secret --secret-id "$(terraform output -raw db_master_user_secret_arn)"
   ```

   From here new connections with the old password fail; pods already connected keep their pooled connections until they recycle.

2. Rebuild the connection string from the new password, exactly as in step 6 of the first deploy (the `host`, `password`, and `put-secret-value` lines for `db_secret_name`).

3. Restart everything that holds a connection string, migrate job included on the next deploy:

   ```
   kubectl rollout restart deployment --namespace stockyard
   kubectl rollout status deployment/stockyard-api --namespace stockyard
   ```

   `/readyz` takes api pods out of the Service while they cannot reach the database, so the gap shows up as 503s, not wrong answers. Do it outside the 02:00 UTC export.

## Replay the DLQ

The `<project>-<env>-dlq-not-empty` alarm fires when a message has failed five receives on `stockyard-events`. Only the projections worker consumes that queue, so the cause is almost always there.

1. See what is waiting and why:

   ```
   cd infra/terraform
   aws sqs get-queue-attributes --queue-url "$(terraform output -raw dlq_url)" \
     --attribute-names ApproximateNumberOfMessages
   kubectl logs deployment/stockyard-projections --namespace stockyard --since=1h
   ```

2. Fix the cause first (a bad deploy, a database outage, a payload the consumer cannot read). Replaying into a consumer that still fails only sends the messages back to the DLQ five receives later.

3. Replay through the api, which moves each message back onto `stockyard-events` with its `type` attribute and deletes it from the DLQ:

   ```
   kubectl port-forward --namespace stockyard service/stockyard-api 8080:80
   curl -fsS -X POST http://localhost:8080/api/v1/outbox/dlq/replay
   ```

   Replaying is safe to repeat: the consumer records each message id in `ProcessedMessage` in the same transaction as the change, so a message that had in fact been applied is acknowledged and skipped.

4. Watch the alarm return to OK and `GET /api/v1/stock/reconcile` report no differences.

SQS can also move the messages itself (`aws sqs start-message-move-task --source-arn <dlq arn>`); the DLQ's redrive allow policy only permits `stockyard-events` as the destination. The api route is preferred because it is the one the integration tests cover.

## Roll back

Images are tagged with the commit SHA and the tags are immutable, so every earlier build is still in ECR (the newest 20 per repository).

* Application only, fastest: undo the last rollout of the affected Deployments.

  ```
  kubectl rollout undo deployment/stockyard-api --namespace stockyard
  kubectl rollout status deployment/stockyard-api --namespace stockyard
  ```

  The next pipeline run puts the newer image back, so follow up with a revert commit on main.

* Through the pipeline: revert the commit on main, or re-run the Deploy stage of the last good run. The plan for infrastructure that has not changed is empty and the rollout applies the older tag.

* Database: migrations only move forward. If a release added a migration, the older image must still work against the newer schema (additive migrations keep that true). If a migration damaged data, restore to a new instance from the point in time just before the deploy and switch the connection string to it:

  ```
  aws rds restore-db-instance-to-point-in-time \
    --source-db-instance-identifier stockyard-dev-sql \
    --target-db-instance-identifier stockyard-dev-sql-restore \
    --restore-time <UTC timestamp before the deploy>
  ```

  The restored instance is outside Terraform until it is imported or the original is replaced; note what was done in the incident record.

* Infrastructure: revert the Terraform change and let the pipeline plan and apply it. Do not edit state by hand to get out of a bad apply.

## Tear down

The fixed cost is roughly 0.28 USD an hour at list price (see the cost table in [platform.md](platform.md#cost)), so the stack comes down when it is not in use. Order matters: workloads first so nothing writes while storage is being emptied, then the guards that stop Terraform, then Terraform.

1. Stop the workloads and release the CSI mounts and synced Secrets:

   ```
   kubectl delete namespace stockyard --wait=true
   ```

2. Lift deletion protection on the database: set `db_deletion_protection = false` in `dev.tfvars`, then

   ```
   cd infra/terraform
   terraform apply -var-file=dev.tfvars
   ```

3. Empty what Terraform will not delete while it holds data. The bucket and the repositories are created without `force_destroy`, deliberately:

   ```
   aws s3 rm "s3://$(terraform output -raw export_bucket)" --recursive
   for repo in stockyard-api stockyard-projections stockyard-notifier stockyard-exporter; do
     ids="$(aws ecr list-images --repository-name "$repo" --query 'imageIds[*]' --output json)"
     [ "$ids" = "[]" ] || aws ecr batch-delete-image --repository-name "$repo" --image-ids "$ids"
   done
   ```

   Copy anything you want to keep (exports for dbt, a last image) before this step.

4. Destroy:

   ```
   terraform destroy -var-file=dev.tfvars
   ```

   RDS takes a final snapshot named `stockyard-dev-sql-final` on the way out. EKS deletes the node group before the cluster, and the add-ons and access entries go with it.

5. Clean up what outlives the destroy:

   * The final DB snapshot, once you are sure you do not need it: `aws rds delete-db-snapshot --db-snapshot-identifier stockyard-dev-sql-final`.
   * The two Secrets Manager secrets sit in their recovery window (7 days by default). To create the stack again under the same names sooner, delete them for good: `aws secretsmanager delete-secret --secret-id stockyard/dev/db --force-delete-without-recovery` (and `stockyard/dev/signing`).
   * Log groups created outside Terraform, if any appeared: `aws logs describe-log-groups --log-group-name-prefix /aws/eks/stockyard-dev` and `--log-group-name-prefix /aws/containerinsights/stockyard-dev`.
   * The state bucket. Keep it if the stack will come back; it costs pennies. Delete it last, after `terraform destroy` has finished writing state.

## What costs money while it exists

| While | What keeps billing | Roughly |
| ----- | ----- | ----- |
| The stack is up, idle or not | EKS control plane, two t3.medium nodes and their volumes, the NAT gateway and its public IPv4 address, the RDS instance and its storage, three secrets, four alarms | about 211 USD a month at list price, from the cost table |
| The stack is up and busy | Add NAT data processing, CloudWatch log ingestion and storage, SQS requests past the free million, S3 requests, Secrets Manager API calls | usage based, not estimated |
| After `terraform destroy` | The final DB snapshot, the state bucket, any log groups left behind, and secrets until their recovery window ends | small, but the snapshot grows with the database; delete it when done |
| Never left running | The pipeline itself on Microsoft-hosted agents, within the free monthly minutes of the organisation | depends on the organisation's plan |

Check the result rather than trusting the list: Cost Explorer filtered on the `Project = stockyard` tag a day after teardown should show nothing but snapshot and S3 storage.
