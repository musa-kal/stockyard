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
