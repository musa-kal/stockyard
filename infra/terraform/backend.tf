# State lives in an S3 bucket created once by hand (see docs/runbook.md).
# Bucket, key, and region are passed at init so no account detail is committed:
#
#   terraform init \
#     -backend-config="bucket=<state bucket>" \
#     -backend-config="key=stockyard/dev.tfstate" \
#     -backend-config="region=us-east-1"
#
# Locking uses the S3 lockfile (use_lockfile), so no DynamoDB table is needed.
terraform {
  backend "s3" {
    encrypt      = true
    use_lockfile = true
  }
}
