# Copy to dev.tfvars (ignored by git) and adjust. Every value here matches the
# default except the ones that need an account-specific answer.

aws_region  = "us-east-1"
project     = "stockyard"
environment = "dev"

vpc_cidr                = "10.40.0.0/16"
availability_zone_count = 2

cluster_version                      = "1.34"
cluster_endpoint_public_access_cidrs = ["203.0.113.10/32"]
cluster_admin_principal_arns = [
  "arn:aws:iam::123456789012:role/stockyard-pipeline",
  "arn:aws:iam::123456789012:role/stockyard-admin",
]

node_instance_types = ["t3.medium"]
node_min_size       = 2
node_desired_size   = 2
node_max_size       = 4

db_instance_class        = "db.t3.small"
db_engine_version        = "16.00"
db_allocated_storage     = 20
db_max_allocated_storage = 50
db_backup_retention_days = 7
db_deletion_protection   = true

export_retention_days       = 90
log_retention_days          = 30
secret_recovery_window_days = 7
alarm_email                 = null

tags = {
  Owner = "platform"
}
